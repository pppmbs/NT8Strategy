// 
// Copyright (C) 2021, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
using NinjaTrader.Gui.SuperDom;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
#endregion

namespace NinjaTrader.NinjaScript.SuperDomColumns
{
	/* 
	 * Works off of several events 
	 *  * MarketDataUpdate from Core
	 *  * MarketDepthUpdate from Core
	 *  * OrderUpdate from Core
	 *  * SelectedAccountChanged from the SuperDom UI (user switching between accounts in the account selector)
	 *  * SelectedAtmStrategySelectionModeChanged from the SuperDom UI (user switching modes in SuperDom properties)
	 *  * SelectedAtmStrategyChanged (user switching selected ATM strategy in strategy selector)
	 * Each price row with an open order will display ONE APQ value. This means that for each price level, track each order's current APQ level.
	 * Only Limit orders are tracked
	 * If there are multiple orders at the same price level, the oldest order's APQ is what's displayed. If it fills or is cancelled, show the next oldest.
	*/
	public class APQ : SuperDomColumn
	{
		private bool																eventsSubscribed;
		private FontFamily															fontFamily;
		private Pen																	gridPen;		
		private double																halfPenWidth;
		private bool																justConnected;
		private ConcurrentDictionary<double, long>									priceApqValues;
		private ConcurrentDictionary<double, ConcurrentDictionary<Cbi.Order, long>>	priceToOrderApqMap; 
		private double																renderWidth;
		private Typeface															typeFace;
		

		private void GetInitialOrderApq(Cbi.Order order, Cbi.OrderState orderState, double limitPrice)
		{
			if (!order.IsLimit) return;

			if (SuperDom.AtmStrategySelectionMode == AtmStrategySelectionMode.DisplaySelectedAtmStrategyOnly)
			{
				if (SuperDom.AtmStrategy != null) 
				{
					AtmStrategy atmStrategy1;
					lock (order.Account.Strategies)
						atmStrategy1 = order.Account.Strategies.FirstOrDefault(s => { lock (s.Orders) return s.Orders.FirstOrDefault(o1 => o1 == order) != null; }) as AtmStrategy;

					if (SuperDom.AtmStrategy != atmStrategy1) return;
				}
			}

			double price = limitPrice;

			if (!Cbi.Order.IsTerminalState(orderState))
			{
				ConcurrentDictionary<Cbi.Order, long> ordersThisPrice;
				// Are there any orders at this price?
				if (priceToOrderApqMap.TryGetValue(price, out ordersThisPrice))
				{
					long apq;
					if (!ordersThisPrice.TryGetValue(order, out apq))
					{
						if (!order.IsSimulatedStop)
						{
							if (order.OrderAction == Cbi.OrderAction.Buy || order.OrderAction == Cbi.OrderAction.BuyToCover)
							{
								NinjaTrader.Data.MarketDepthRow lr = null;
								lock (SuperDom.MarketDepth.Instrument.SyncMarketDepth)
									lr = SuperDom.MarketDepth.Bids.FirstOrDefault(b => b.Price == price);
								if (lr != null)
									apq = lr.Volume + 1;

								ordersThisPrice.TryAdd(order, apq);
							}
							else
							{
								NinjaTrader.Data.MarketDepthRow lr = null;
								lock (SuperDom.MarketDepth.Instrument.SyncMarketDepth)
									lr = SuperDom.MarketDepth.Asks.FirstOrDefault(b => b.Price == price);
								if (lr != null)
									apq = lr.Volume + 1;

								ordersThisPrice.TryAdd(order, apq);
							}
							
							// Remove order from any other prices it might reside in
							RemoveOrder(order, price);
							// Update APQ value to be written to screen at this price
							UpdateApqValuesForScreen();
						}
					}
				}
				else
				{
					long apq = 0;
					if (!order.IsSimulatedStop)
					{
						if (order.OrderAction == Cbi.OrderAction.Buy || order.OrderAction == Cbi.OrderAction.BuyToCover)
						{
							NinjaTrader.Data.MarketDepthRow lr = null;
							lock (SuperDom.MarketDepth.Instrument.SyncMarketDepth) 
								lr = SuperDom.MarketDepth.Bids.FirstOrDefault(b => b.Price == price);
							if (lr != null)
								apq = lr.Volume + 1;
						}
						else
						{
							NinjaTrader.Data.MarketDepthRow lr = null;
							lock(SuperDom.MarketDepth.Instrument.SyncMarketDepth)
								lr = SuperDom.MarketDepth.Asks.FirstOrDefault(b => b.Price == price);
							if (lr != null)
								apq = lr.Volume + 1;
						}

						ordersThisPrice = new ConcurrentDictionary<Cbi.Order, long>();
						if (ordersThisPrice.TryAdd(order, apq) && priceToOrderApqMap.TryAdd(price, ordersThisPrice))
						{
							// Remove order from any other prices it might reside in
							RemoveOrder(order, price);
							// Update APQ value to be written to screen at this price
							UpdateApqValuesForScreen();
						}
					}
				}
			}
			else	// Remove terminal orders
			{
				if (!order.IsMarket)
				{
					// Remove order from any other prices it might reside in
					RemoveOrder(order);
					// Update APQ value to be written to screen at this price
					UpdateApqValuesForScreen();
				}
			}
		}

		protected override void OnMarketData(Data.MarketDataEventArgs marketUpdate)
		{
			if (marketUpdate.MarketDataType != Data.MarketDataType.Last) return;
			
			Data.MarketDataEventArgs mu = marketUpdate;	// Access to modified closure
			if (justConnected)
			{
				if (SuperDom.Account == null) return;
				// Get the orders already open and assign APQ values to them
				lock (SuperDom.Account.Orders)
					foreach (Cbi.Order order in SuperDom.Account.Orders)
						GetInitialOrderApq(order, order.OrderState, order.LimitPrice);

				justConnected = false;
				return;
			}
			
			long currentApqValue;
			if (priceApqValues.TryGetValue(mu.Price, out currentApqValue))
			{
				// If the current volume has dropped below the previous value, update APQ value 
				lock (SuperDom.Rows)
				{
					PriceRow row = SuperDom.Rows.FirstOrDefault(r => r.Price == mu.Price);
					if (row != null)  
					{
						if (row.BuyOrders.Count > 0)
						{
							long newVal = currentApqValue - mu.Volume;
							if (currentApqValue != 0 && newVal < currentApqValue)
							{
								priceApqValues.TryUpdate(mu.Price,														// Key
													currentApqValue > 1 ? (newVal >= 1 ? newVal : 1) : currentApqValue,	// New value
														currentApqValue);												// Comparison value
								
								ConcurrentDictionary<Cbi.Order, long> ordersThisPrice;
								if (priceToOrderApqMap.TryGetValue(mu.Price, out ordersThisPrice))
								{
									foreach (Cbi.Order key in ordersThisPrice.Keys)
									{
										if (newVal < ordersThisPrice[key])
											ordersThisPrice.TryUpdate(key, newVal, ordersThisPrice[key]);
									}
								}
							}
						}
						else if (row.SellOrders.Count == 0)
						{
							long oldDepth;
							priceApqValues.TryRemove(mu.Price, out oldDepth);
						}

						if (row.SellOrders.Count > 0)
						{
							long newVal = currentApqValue - mu.Volume;
							if (currentApqValue != 0 && newVal < currentApqValue)
							{
								priceApqValues.TryUpdate(mu.Price,											// Key
									 currentApqValue > 1 ? (newVal >= 1 ? newVal : 1) : currentApqValue,	// New value
										currentApqValue);													// Comparison value
								
								ConcurrentDictionary<Cbi.Order, long> ordersThisPrice;
								if (priceToOrderApqMap.TryGetValue(mu.Price, out ordersThisPrice))
								{
									foreach (Cbi.Order key in ordersThisPrice.Keys)
									{
										if (newVal < ordersThisPrice[key])
											ordersThisPrice.TryUpdate(key, newVal, ordersThisPrice[key]);
									}
								}
							}
						}
						else if (row.BuyOrders.Count == 0)
						{
							long oldDepth;
							priceApqValues.TryRemove(mu.Price, out oldDepth);
						}
					}
				}
			}
		}

		private void OnMarketDepthUpdate(object sender, Data.MarketDepthEventArgs e)
		{
			if (justConnected)
			{
				if (SuperDom.Account == null) return;
				// Get the orders already open and assign APQ values to them
				lock (SuperDom.Account.Orders)
					foreach (Cbi.Order order in SuperDom.Account.Orders)
						GetInitialOrderApq(order, order.OrderState, order.LimitPrice);

				justConnected = false;
				return;
			}
			
			lock (SuperDom.Rows)
			{
				if (SuperDom.MarketDepth != null)
				{
					if (SuperDom.MarketDepth.Bids != null)
					{
						foreach (LadderRow bid in SuperDom.MarketDepth.Bids)
						{
							PriceRow row = SuperDom.Rows.FirstOrDefault(r => r.Price == bid.Price);
							if (row == null) continue;

							long currentApqValue;
							bool gotCurrentApq = priceApqValues.TryGetValue(bid.Price, out currentApqValue);

							if ((!gotCurrentApq || bid.Volume < currentApqValue - 1) && bid.Volume >= 1)
							{
								// If the current volume has dropped below the previous value, update APQ value 
								if (row.BuyOrders.Count(o => o.IsLimit) > 0)
								{
									long newVal = priceApqValues.AddOrUpdate(bid.Price, bid.Volume + 1,
										(key, oldValue) => oldValue > 1 ?
											oldValue - (currentApqValue - bid.Volume) >= 1 ?
												oldValue - (currentApqValue - bid.Volume) : 1 : oldValue);

									ConcurrentDictionary<Cbi.Order, long> ordersThisPrice;
									if (priceToOrderApqMap.TryGetValue(bid.Price, out ordersThisPrice))
										foreach (Cbi.Order key in ordersThisPrice.Keys)
											if (newVal < ordersThisPrice[key])
												ordersThisPrice.TryUpdate(key, newVal, ordersThisPrice[key]);
								}
								else
								{
									long oldDepth;
									priceApqValues.TryRemove(bid.Price, out oldDepth);
								}
							}
						}
					}

					if (SuperDom.MarketDepth.Asks != null)
					{
						foreach (LadderRow ask in SuperDom.MarketDepth.Asks)
						{
							PriceRow row = SuperDom.Rows.FirstOrDefault(r => r.Price == ask.Price);
							if (row == null) continue;

							long currentApqValue;
							bool gotCurrentApq = priceApqValues.TryGetValue(ask.Price, out currentApqValue);

							if ((!gotCurrentApq || ask.Volume < currentApqValue - 1) && ask.Volume >= 1)
							{
								// If the current volume has dropped below the previous value, update APQ value 
								if (row.SellOrders.Count(o => o.IsLimit) > 0)
								{
									long newVal = priceApqValues.AddOrUpdate(ask.Price, ask.Volume + 1,
										(key, oldValue) => oldValue > 1 ?
											oldValue - (currentApqValue - ask.Volume) >= 1 ?
												oldValue - (currentApqValue - ask.Volume) : 1 : oldValue);

									ConcurrentDictionary<Cbi.Order, long> ordersThisPrice;
									// Are there any orders at this price?
									if (priceToOrderApqMap.TryGetValue(ask.Price, out ordersThisPrice))
										foreach (Cbi.Order key in ordersThisPrice.Keys)
											if (newVal < ordersThisPrice[key])
												ordersThisPrice.TryUpdate(key, newVal, ordersThisPrice[key]);
								}
								else
								{
									long oldDepth;
									priceApqValues.TryRemove(ask.Price, out oldDepth);
								}
							}
						}
					}
				}
			}
		}

		protected override void OnOrderUpdate(Cbi.OrderEventArgs orderUpdate)
		{
			if (orderUpdate.Order.Instrument != SuperDom.Instrument || orderUpdate.Order.Account != SuperDom.Account || justConnected) return;

			// Be sure to use the OrderUpdateEventArgs values for order state, limit price, and stop price, as this guarantees that order state changes 
			// are processed in the right order. The underlying order object may be further ahead than the order update event.
			GetInitialOrderApq(orderUpdate.Order, orderUpdate.OrderState, orderUpdate.LimitPrice);
		}
		
		protected override void OnRender(DrawingContext dc, double renderWidth)
		{
			// This may be true if the UI for a column hasn't been loaded yet (e.g., restoring multiple tabs from workspace won't load each tab until it's clicked by the user)
			if (gridPen == null)
			{
				if (UiWrapper != null && PresentationSource.FromVisual(UiWrapper) != null)
				{
					Matrix m			= PresentationSource.FromVisual(UiWrapper).CompositionTarget.TransformToDevice;
					double dpiFactor	= 1 / m.M11;
					gridPen				= new Pen(Application.Current.TryFindResource("BorderThinBrush") as Brush,  1 * dpiFactor);
					halfPenWidth		= gridPen.Thickness * 0.5;
				}
			}

			this.renderWidth		= renderWidth;
			double verticalOffset	= -gridPen.Thickness;
			double pixelsPerDip		= VisualTreeHelper.GetDpi(UiWrapper).PixelsPerDip;

			lock (SuperDom.Rows)
				foreach (PriceRow row in SuperDom.Rows)
				{
					// Draw cell
					if (this.renderWidth - halfPenWidth >= 0)
					{
						Rect rect = new Rect(-halfPenWidth, verticalOffset, this.renderWidth - halfPenWidth, SuperDom.ActualRowHeight);

						// Create a guidelines set
						GuidelineSet guidelines = new GuidelineSet();
						guidelines.GuidelinesX.Add(rect.Left	+ halfPenWidth);
						guidelines.GuidelinesX.Add(rect.Right	+ halfPenWidth);
						guidelines.GuidelinesY.Add(rect.Top		+ halfPenWidth);
						guidelines.GuidelinesY.Add(rect.Bottom	+ halfPenWidth);

						dc.PushGuidelineSet(guidelines);
						dc.DrawRectangle(BackColor, null, rect);
						dc.DrawLine(gridPen, new Point(-gridPen.Thickness, rect.Bottom), new Point(this.renderWidth - halfPenWidth, rect.Bottom));
						dc.DrawLine(gridPen, new Point(rect.Right, verticalOffset), new Point(rect.Right, rect.Bottom));
						// Print APQ value - remember to set MaxTextWidth so text doesn't spill into another column
						long apq;
						if (priceApqValues.TryGetValue(row.Price, out apq) && apq > 0)
						{
							fontFamily				= SuperDom.Font.Family;
							typeFace				= new Typeface(fontFamily, SuperDom.Font.Italic ? FontStyles.Italic : FontStyles.Normal, SuperDom.Font.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);

							if (this.renderWidth - 6 > 0)
							{
								FormattedText noteText = new FormattedText(apq != long.MinValue ? apq.ToString(Core.Globals.GeneralOptions.CurrentCulture) : "N/A", Core.Globals.GeneralOptions.CurrentCulture, FlowDirection.LeftToRight, typeFace, SuperDom.Font.Size, ForeColor, pixelsPerDip) { MaxLineCount = 1, MaxTextWidth = this.renderWidth - 6, Trimming = TextTrimming.CharacterEllipsis };
								dc.DrawText(noteText, new Point(0 + 4, verticalOffset + (SuperDom.ActualRowHeight - noteText.Height) / 2));
							}
						}

						dc.Pop();
						verticalOffset	+= SuperDom.ActualRowHeight;
					}
				}
		}

		private void OnSelectedAccountChanged(object sender, EventArgs e)
		{
			ResetApqCollections();

			lock (SuperDom.Rows)
				foreach (PriceRow row in SuperDom.Rows)
				{
					foreach (Cbi.Order order in row.BuyOrders)
						GetInitialOrderApq(order, order.OrderState, order.LimitPrice);

					foreach (Cbi.Order order in row.SellOrders)
						GetInitialOrderApq(order, order.OrderState, order.LimitPrice);
				}
		}

		private void OnSelectedAtmStrategyChanged(object sender, EventArgs e)
		{
			if (SuperDom.AtmStrategySelectionMode == AtmStrategySelectionMode.DisplaySelectedAtmStrategyOnly)
			{
				ResetApqCollections();

				lock (SuperDom.Rows)
					foreach (PriceRow row in SuperDom.Rows)
					{
						foreach (Cbi.Order order in row.BuyOrders)
							GetInitialOrderApq(order, order.OrderState, order.LimitPrice);

						foreach (Cbi.Order order in row.SellOrders)
							GetInitialOrderApq(order, order.OrderState, order.LimitPrice);
					}
			}
		}

		private void OnSelectedAtmStrategySelectionModeChanged(object sender, EventArgs e)
		{
			ResetApqCollections();

			lock (SuperDom.Rows)
				foreach (PriceRow row in SuperDom.Rows)
				{
					foreach (Cbi.Order order in row.BuyOrders)
						GetInitialOrderApq(order, order.OrderState, order.LimitPrice);

					foreach (Cbi.Order order in row.SellOrders)
						GetInitialOrderApq(order, order.OrderState, order.LimitPrice);
				}
		}

		protected override void OnStateChange()
		{
			if (State == NinjaTrader.NinjaScript.State.SetDefaults)
			{
				Name							= NinjaTrader.Custom.Resource.NinjaScriptSuperDomColumnApq;
				Description						= NinjaTrader.Custom.Resource.NinjaScriptSuperDomColumnDescriptionApq;
				DefaultWidth					= 100;
				PreviousWidth					= -1;
				IsDataSeriesRequired			= false;
				BackColor						= Application.Current.TryFindResource("brushPriceColumnBackground") as Brush;
				ForeColor						= Application.Current.TryFindResource("FontControlBrush") as SolidColorBrush;
				priceApqValues					= new ConcurrentDictionary<double, long>();
				priceToOrderApqMap				= new ConcurrentDictionary<double, ConcurrentDictionary<Cbi.Order, long>>();
			}
			else if (State == NinjaTrader.NinjaScript.State.Configure)
			{
				if (UiWrapper != null && PresentationSource.FromVisual(UiWrapper) != null)
				{ 
					Matrix m			= PresentationSource.FromVisual(UiWrapper).CompositionTarget.TransformToDevice;
					double dpiFactor	= 1 / m.M11;
					gridPen				= new Pen(Application.Current.TryFindResource("BorderThinBrush") as Brush,  1 * dpiFactor);
					halfPenWidth		= gridPen.Thickness * 0.5;
				}
			}
			else if (State == NinjaTrader.NinjaScript.State.Active)
			{
				if (!eventsSubscribed && SuperDom.MarketDepth != null)
				{
					WeakEventManager<Data.MarketDepth<LadderRow>, Data.MarketDepthEventArgs>.AddHandler(SuperDom.MarketDepth, "Update", OnMarketDepthUpdate);
					WeakEventManager<SuperDomViewModel, EventArgs>.AddHandler(SuperDom, "SelectedAccountChanged", OnSelectedAccountChanged);
					WeakEventManager<SuperDomViewModel, EventArgs>.AddHandler(SuperDom, "SelectedAtmStrategyChanged", OnSelectedAtmStrategyChanged);
					WeakEventManager<SuperDomViewModel, EventArgs>.AddHandler(SuperDom, "SelectedAtmStrategySelectionModeChanged", OnSelectedAtmStrategySelectionModeChanged);
					eventsSubscribed = true;
				}

				justConnected										= true;
			}
			else if (State == NinjaTrader.NinjaScript.State.Terminated)
			{
				if (SuperDom == null) return;

				if (SuperDom.MarketDepth != null && eventsSubscribed)
				{
					WeakEventManager<Data.MarketDepth<LadderRow>, Data.MarketDepthEventArgs>.RemoveHandler(SuperDom.MarketDepth, "Update", OnMarketDepthUpdate);
					WeakEventManager<SuperDomViewModel, EventArgs>.RemoveHandler(SuperDom, "SelectedAccountChanged", OnSelectedAccountChanged);
					WeakEventManager<SuperDomViewModel, EventArgs>.RemoveHandler(SuperDom, "SelectedAtmStrategyChanged", OnSelectedAtmStrategyChanged);
					WeakEventManager<SuperDomViewModel, EventArgs>.RemoveHandler(SuperDom, "SelectedAtmStrategySelectionModeChanged", OnSelectedAtmStrategySelectionModeChanged);
					eventsSubscribed = false;
				}
			}
		}

		private void RemoveOrder(Cbi.Order order, double excludePrice = double.MinValue)
		{
			try
			{
				// Remove order from any other prices it might reside in
				KeyValuePair<double, ConcurrentDictionary<Cbi.Order, long>>[] kvps = priceToOrderApqMap.ToArray();
				foreach (KeyValuePair<double, ConcurrentDictionary<Cbi.Order, long>> kvp in kvps)
				{
					if (excludePrice != double.MinValue)
					{
						if (kvp.Key != excludePrice)
						{
							long oldApq;
							kvp.Value.TryRemove(order, out oldApq);
						}
					}
					else
					{
						long oldApq;
						kvp.Value.TryRemove(order, out oldApq);
					}
				}
			}
			catch (Exception ex)
			{
				LogAndPrint(typeof(NinjaTrader.Custom.Resource), "SuperDomColumnException", new[] { Name, "RemoveOrder", ex.Message }, Cbi.LogLevel.Error);
			}
		}

		private void ResetApqCollections()
		{
			priceApqValues.Clear();
			priceToOrderApqMap.Clear();
		}

		private void UpdateApqValuesForScreen()
		{
			try
			{
				// Get all the orders for the given price
				// Sort them by time
				// APQ = the apq value for the oldest order in the dictionary
				KeyValuePair<double, ConcurrentDictionary<Cbi.Order, long>>[] kvps = priceToOrderApqMap.ToArray();
				foreach (KeyValuePair<double, ConcurrentDictionary<Cbi.Order, long>> kvp in kvps)
				{
					ConcurrentDictionary<Cbi.Order, long>	ordersThisPrice;
					double									price				= kvp.Key;
					if (priceToOrderApqMap.TryGetValue(price, out ordersThisPrice))
					{
						List<Cbi.Order> orders = ordersThisPrice.Select(o => o.Key).ToList();
						if (orders.Count > 0)
						{
							orders.Sort((a, b) => b.Time.CompareTo(a.Time));
							Cbi.Order oldest = orders[orders.Count - 1];
						
							priceApqValues.AddOrUpdate(price, ordersThisPrice[oldest], (key, oldValue) => ordersThisPrice[oldest]);
						}
						else
						{
							long oldValue;
							priceApqValues.TryRemove(price, out oldValue);
							priceToOrderApqMap.TryRemove(price, out ordersThisPrice);
						}
					}
					else
					{
						long oldValue;
						priceApqValues.TryRemove(price, out oldValue);
					}
				}
			}
			catch (Exception ex)
			{
				LogAndPrint(typeof(NinjaTrader.Custom.Resource), "SuperDomColumnException", new[] { Name, "UpdateApqValuesForScreen", ex.Message }, Cbi.LogLevel.Error);
			}
		}

		#region Properties

		#region Colors
		[XmlIgnore]
		[Display(ResourceType = typeof(Resource), Name = "NinjaScriptColumnBaseBackground", GroupName = "PropertyCategoryVisual", Order = 105)]
		public Brush BackColor
		{ get; set; }

		[Browsable(false)]
		public string BackgroundBrushSerialize
		{
			get { return NinjaTrader.Gui.Serialize.BrushToString(BackColor, "brushPriceColumnBackground"); }
			set { BackColor = NinjaTrader.Gui.Serialize.StringToBrush(value, "brushPriceColumnBackground"); }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Resource), Name = "NinjaScriptColumnBaseForeground", GroupName = "PropertyCategoryVisual", Order = 111)]
		public Brush ForeColor
		{ get; set; }

		[Browsable(false)]
		public string ForeColorSerialize
		{
			get { return NinjaTrader.Gui.Serialize.BrushToString(ForeColor); }
			set { ForeColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
		}	
		#endregion

		#endregion
	}
}
