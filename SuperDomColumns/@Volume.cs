// 
// Copyright (C) 2022, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
using NinjaTrader.Data;
using NinjaTrader.Gui.SuperDom;
using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
#endregion

namespace NinjaTrader.NinjaScript.SuperDomColumns
{
	public class Volume : SuperDomColumn
	{
		private readonly	object			barsSync					= new object();
		private				bool			clearLoadingSent;
		private				FontFamily		fontFamily;
		private				FontStyle		fontStyle;
		private				FontWeight		fontWeight;
		private				Pen				gridPen;			
		private				double			halfPenWidth;
		private				bool			heightUpdateNeeded;
		private				int				lastMaxIndex				= -1;
		private				long			maxVolume;
		private				bool			mouseEventsSubscribed;
		private				double			textHeight;
		private				Point			textPosition				= new Point(4, 0);
		private				string			tradingHoursSerializable	= string.Empty;
		private				long			totalBuyVolume;
		private				long			totalLastVolume;
		private				long			totalSellVolume;
		private				Typeface		typeFace;


		private void OnBarsUpdate(object sender, BarsUpdateEventArgs e)
		{
			if (State == State.Active && SuperDom != null && SuperDom.IsConnected)
			{
				if (SuperDom.IsReloading)
				{
					OnPropertyChanged();
					return;
				}

				BarsUpdateEventArgs barsUpdate = e;
				lock (barsSync)
				{
					int currentMaxIndex = barsUpdate.MaxIndex;

					for (int i = lastMaxIndex + 1; i <= currentMaxIndex; i++)
					{
						if (barsUpdate.BarsSeries.GetIsFirstBarOfSession(i))
						{
							// If a new session starts, clear out the old values and start fresh
							maxVolume		= 0;
							totalBuyVolume	= 0;
							totalLastVolume	= 0;
							totalSellVolume	= 0;
							Sells.Clear();
							Buys.Clear();
							LastVolumes.Clear();
						}

						double	ask		= barsUpdate.BarsSeries.GetAsk(i);
						double	bid		= barsUpdate.BarsSeries.GetBid(i);
						double	close	= barsUpdate.BarsSeries.GetClose(i);
						long	volume	= barsUpdate.BarsSeries.GetVolume(i);

						if (ask != double.MinValue && close >= ask)
						{
							Buys.AddOrUpdate(close, volume, (price, oldVolume) => oldVolume + volume);
							totalBuyVolume += volume;
						}
						else if (bid != double.MinValue && close <= bid)
						{
							Sells.AddOrUpdate(close, volume, (price, oldVolume) => oldVolume + volume);
							totalSellVolume += volume;
						}

						long newVolume;
						LastVolumes.AddOrUpdate(close, newVolume = volume, (price, oldVolume) => newVolume = oldVolume + volume);
						totalLastVolume += volume;

						if (newVolume > maxVolume)
							maxVolume = newVolume;
					}

					lastMaxIndex = barsUpdate.MaxIndex;
					if (!clearLoadingSent)
					{
						SuperDom.Dispatcher.InvokeAsync(() => SuperDom.ClearLoadingString());
						clearLoadingSent = true;
					}
				}
			}
		}

		private void OnMouseLeave(object sender, MouseEventArgs e)
		{
			OnPropertyChanged();
		}

		private void OnMouseEnter(object sender, MouseEventArgs e)
		{
			OnPropertyChanged();
		}

		private void OnMouseMove(object sender, MouseEventArgs e)
		{
			OnPropertyChanged();
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
					gridPen				= new Pen(Application.Current.TryFindResource("BorderThinBrush") as Brush, 1 * dpiFactor);
					halfPenWidth		= gridPen.Thickness * 0.5;
				}
			}

			if (fontFamily != SuperDom.Font.Family
				|| (SuperDom.Font.Italic && fontStyle != FontStyles.Italic)
				|| (!SuperDom.Font.Italic && fontStyle == FontStyles.Italic)
				|| (SuperDom.Font.Bold && fontWeight != FontWeights.Bold)
				|| (!SuperDom.Font.Bold && fontWeight == FontWeights.Bold))
			{
				// Only update this if something has changed
				fontFamily	= SuperDom.Font.Family;
				fontStyle	= SuperDom.Font.Italic ? FontStyles.Italic : FontStyles.Normal;
				fontWeight	= SuperDom.Font.Bold ? FontWeights.Bold : FontWeights.Normal;
				typeFace	= new Typeface(fontFamily, fontStyle, fontWeight, FontStretches.Normal);
				heightUpdateNeeded = true;
			}

			double verticalOffset	= -gridPen.Thickness;
			double pixelsPerDip		= VisualTreeHelper.GetDpi(UiWrapper).PixelsPerDip;

			lock (SuperDom.Rows)
				foreach (PriceRow row in SuperDom.Rows)
				{
					if (renderWidth - halfPenWidth >= 0)
					{
						// Draw cell
						Rect rect = new Rect(-halfPenWidth, verticalOffset, renderWidth - halfPenWidth, SuperDom.ActualRowHeight);

						// Create a guidelines set
						GuidelineSet guidelines = new GuidelineSet();
						guidelines.GuidelinesX.Add(rect.Left	+ halfPenWidth);
						guidelines.GuidelinesX.Add(rect.Right	+ halfPenWidth);
						guidelines.GuidelinesY.Add(rect.Top		+ halfPenWidth);
						guidelines.GuidelinesY.Add(rect.Bottom	+ halfPenWidth);

						dc.PushGuidelineSet(guidelines);
						dc.DrawRectangle(BackColor, null, rect);
						dc.DrawLine(gridPen, new Point(-gridPen.Thickness, rect.Bottom), new Point(renderWidth - halfPenWidth, rect.Bottom));
						dc.DrawLine(gridPen, new Point(rect.Right, verticalOffset), new Point(rect.Right, rect.Bottom));
						//dc.Pop();

						if (SuperDom.IsConnected 
							&& !SuperDom.IsReloading
							&& State == NinjaTrader.NinjaScript.State.Active)
						{
							// Draw proportional volume bar
							long	buyVolume		= 0;
							long	sellVolume		= 0;
							long	totalRowVolume	= 0;
							long	totalVolume		= 0;

							if (VolumeType == VolumeType.Standard)
							{
								if (LastVolumes.TryGetValue(row.Price, out totalRowVolume))
									totalVolume = totalLastVolume;
								else
								{
									verticalOffset += SuperDom.ActualRowHeight;
									continue;
								}
							}
							else if (VolumeType == VolumeType.BuySell)
							{
								bool gotBuy		= Sells.TryGetValue(row.Price, out sellVolume);
								bool gotSell	= Buys.TryGetValue(row.Price, out buyVolume);
								if (gotBuy || gotSell)
								{
									totalRowVolume	= sellVolume + buyVolume;
									totalVolume		= totalBuyVolume + totalSellVolume;
								}
								else
								{
									verticalOffset += SuperDom.ActualRowHeight;
									continue;
								}
							}

							double totalWidth = renderWidth * ((double)totalRowVolume / maxVolume); 
							if (totalWidth - gridPen.Thickness >= 0)
							{
								if (VolumeType == VolumeType.Standard)
								{
									dc.DrawRectangle(BarColor, null, new Rect(0, verticalOffset + halfPenWidth, totalWidth == renderWidth ? totalWidth - gridPen.Thickness * 1.5 : totalWidth - halfPenWidth, rect.Height - gridPen.Thickness));
								}
								else if (VolumeType == VolumeType.BuySell)
								{
									double buyWidth = totalWidth * ((double)buyVolume / totalRowVolume);

									// Draw total volume, then draw buys on top
									if (totalWidth - halfPenWidth >= 0)
										dc.DrawRectangle(SellColor,	null, new Rect(0, verticalOffset + halfPenWidth, totalWidth == renderWidth ? totalWidth - gridPen.Thickness * 1.5 : totalWidth - halfPenWidth, rect.Height - gridPen.Thickness));
									if (buyWidth - halfPenWidth >= 0)
										dc.DrawRectangle(BuyColor, null, new Rect(0, verticalOffset + halfPenWidth, buyWidth - halfPenWidth, rect.Height - gridPen.Thickness));
								}
							}
							// Print volume value - remember to set MaxTextWidth so text doesn't spill into another column
							if (totalRowVolume > 0)
							{
								string volumeString = string.Empty;
								if (DisplayType == DisplayType.Volume)
								{
									if (SuperDom.Instrument.MasterInstrument.InstrumentType == Cbi.InstrumentType.CryptoCurrency)
										volumeString = Core.Globals.ToCryptocurrencyVolume(totalRowVolume).ToString(Core.Globals.GeneralOptions.CurrentCulture);
									else
										volumeString = totalRowVolume.ToString(Core.Globals.GeneralOptions.CurrentCulture);
								}
								else if (DisplayType == DisplayType.Percent)
									volumeString = ((double)totalRowVolume / totalVolume).ToString("P1", Core.Globals.GeneralOptions.CurrentCulture);

								if (renderWidth - 6 > 0)
								{
									if (DisplayText || rect.Contains(Mouse.GetPosition(UiWrapper)))
									{
										FormattedText volumeText = new FormattedText(volumeString, Core.Globals.GeneralOptions.CurrentCulture, FlowDirection.LeftToRight, typeFace, SuperDom.Font.Size, ForeColor, pixelsPerDip) { MaxLineCount = 1, MaxTextWidth = renderWidth - 6, Trimming = TextTrimming.CharacterEllipsis };

										// Getting the text height is expensive, so only update it if something's changed
										if (heightUpdateNeeded)
										{
											textHeight = volumeText.Height;
											heightUpdateNeeded = false;
										}

										textPosition.Y = verticalOffset + (SuperDom.ActualRowHeight - textHeight) / 2;
										dc.DrawText(volumeText, textPosition);
									}
								}
							}
							verticalOffset += SuperDom.ActualRowHeight;
						}
						else
							verticalOffset += SuperDom.ActualRowHeight;

						dc.Pop();
					}
				}
		}

		public override void OnRestoreValues()
		{
			// Forecolor and standard bar color
			bool restored = false;

			SolidColorBrush defaultForeColor = Application.Current.FindResource("immutableBrushVolumeColumnForeground") as SolidColorBrush;
			if (	(ForeColor			as SolidColorBrush).Color == (ImmutableForeColor as SolidColorBrush).Color
				&&	(ImmutableForeColor as SolidColorBrush).Color != defaultForeColor.Color)
			{
				ForeColor			= defaultForeColor;
				ImmutableForeColor	= defaultForeColor;
				restored			= true;
			}

			SolidColorBrush defaultBarColor = Application.Current.FindResource("immutableBrushVolumeColumnBackground") as SolidColorBrush;
			if ((BarColor as SolidColorBrush).Color == (ImmutableBarColor as SolidColorBrush).Color
				&& (ImmutableBarColor as SolidColorBrush).Color != defaultBarColor.Color)
			{
				BarColor			= defaultBarColor;
				ImmutableBarColor	= defaultBarColor;
				restored			= true;
			}

			if (restored) OnPropertyChanged();
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name					= NinjaTrader.Custom.Resource.NinjaScriptSuperDomColumnVolume;
				Description				= NinjaTrader.Custom.Resource.NinjaScriptSuperDomColumnDescriptionVolume;
				Buys					= new ConcurrentDictionary<double, long>();
				BackColor				= Application.Current.TryFindResource("brushPriceColumnBackground") as Brush;
				BarColor				= Application.Current.TryFindResource("brushVolumeColumnBackground") as Brush;
				BuyColor				= Brushes.DarkCyan;
				DefaultWidth			= 160;
				PreviousWidth			= -1;
				DisplayText				= false;
				DisplayType				= DisplayType.Volume;
				ForeColor				= Application.Current.TryFindResource("brushVolumeColumnForeground") as Brush;
				ImmutableBarColor		= Application.Current.TryFindResource("immutableBrushVolumeColumnBackground") as Brush;
				ImmutableForeColor		= Application.Current.TryFindResource("immutableBrushVolumeColumnForeground") as Brush;
				IsDataSeriesRequired	= true;
				LastVolumes				= new ConcurrentDictionary<double, long>();
				SellColor				= Brushes.Crimson;
				Sells					= new ConcurrentDictionary<double, long>();
				VolumeType				= VolumeType.Standard;
			}
			else if (State == State.Configure)
			{
				if (UiWrapper != null && PresentationSource.FromVisual(UiWrapper) != null)
				{ 
					Matrix m			= PresentationSource.FromVisual(UiWrapper).CompositionTarget.TransformToDevice;
					double dpiFactor	= 1 / m.M11;
					gridPen				= new Pen(Application.Current.TryFindResource("BorderThinBrush") as Brush,  1 * dpiFactor);
					halfPenWidth		= gridPen.Thickness * 0.5;
				}

				if (SuperDom.Instrument != null && SuperDom.IsConnected)
				{
					BarsPeriod bp		= new BarsPeriod
					{
						MarketDataType = MarketDataType.Last, 
						BarsPeriodType = BarsPeriodType.Tick, 
						Value = 1
					};

					SuperDom.Dispatcher.InvokeAsync(() => SuperDom.SetLoadingString());
					clearLoadingSent = false;

					if (BarsRequest != null)
					{
						BarsRequest.Update -= OnBarsUpdate;
						BarsRequest = null;
					}

					BarsRequest = new BarsRequest(SuperDom.Instrument,
						Cbi.Connection.PlaybackConnection != null ? Cbi.Connection.PlaybackConnection.Now : Core.Globals.Now,
						Cbi.Connection.PlaybackConnection != null ? Cbi.Connection.PlaybackConnection.Now : Core.Globals.Now);

					BarsRequest.BarsPeriod		= bp;
					BarsRequest.TradingHours	= (TradingHoursSerializable.Length == 0 || TradingHours.Get(TradingHoursSerializable) == null) ? SuperDom.Instrument.MasterInstrument.TradingHours : TradingHours.Get(TradingHoursSerializable);
					BarsRequest.Update			+= OnBarsUpdate;

					BarsRequest.Request((request, errorCode, errorMessage) =>
						{
							// Make sure this isn't a bars callback from another column instance
							if (request != BarsRequest)
								return;

							lastMaxIndex	= 0;
							maxVolume		= 0;
							totalBuyVolume	= 0;
							totalLastVolume = 0;
							totalSellVolume = 0;
							Sells.Clear();
							Buys.Clear();
							LastVolumes.Clear();

							if (State >= NinjaTrader.NinjaScript.State.Terminated)
								return;

							if (errorCode == Cbi.ErrorCode.UserAbort)
							{
								if (State <= NinjaTrader.NinjaScript.State.Terminated)
									if (SuperDom != null && !clearLoadingSent)
									{
										SuperDom.Dispatcher.InvokeAsync(() => SuperDom.ClearLoadingString());
										clearLoadingSent = true;
									}
										
								request.Update -= OnBarsUpdate;
								request.Dispose();
								request = null;
								return;
							}
							
							if (errorCode != Cbi.ErrorCode.NoError)
							{
								request.Update -= OnBarsUpdate;
								request.Dispose();
								request = null;
								if (SuperDom != null && !clearLoadingSent)
								{
									SuperDom.Dispatcher.InvokeAsync(() => SuperDom.ClearLoadingString());
									clearLoadingSent = true;
								}
							}
							else if (errorCode == Cbi.ErrorCode.NoError)
							{
								try
								{
									SessionIterator	superDomSessionIt		= new SessionIterator(request.Bars);
									bool			includesEndTimeStamp	= request.Bars.BarsType.IncludesEndTimeStamp(false);
									if (superDomSessionIt.IsInSession(Cbi.Connection.PlaybackConnection != null ? Cbi.Connection.PlaybackConnection.Now : Core.Globals.Now, includesEndTimeStamp, request.Bars.BarsType.IsIntraday))
									{
										for (int i = 0; i < request.Bars.Count; i++)
										{
											DateTime time = request.Bars.BarsSeries.GetTime(i);
											if ((includesEndTimeStamp && time <= superDomSessionIt.ActualSessionBegin) || (!includesEndTimeStamp && time < superDomSessionIt.ActualSessionBegin))
												continue;

											double	ask		= request.Bars.BarsSeries.GetAsk(i);
											double	bid		= request.Bars.BarsSeries.GetBid(i);
											double	close	= request.Bars.BarsSeries.GetClose(i);
											long	volume	= request.Bars.BarsSeries.GetVolume(i);

											if (ask != double.MinValue && close >= ask)
											{
												Buys.AddOrUpdate(close, volume, (price, oldVolume) => oldVolume + volume);
												totalBuyVolume += volume;
											}
											else if (bid != double.MinValue && close <= bid)
											{
												Sells.AddOrUpdate(close, volume, (price, oldVolume) => oldVolume + volume);
												totalSellVolume += volume;
											}

											long newVolume;
											LastVolumes.AddOrUpdate(close, newVolume = volume, (price, oldVolume) => newVolume = oldVolume + volume);
											totalLastVolume += volume;

											if (newVolume > maxVolume)
												maxVolume = newVolume;
										}

										lastMaxIndex = request.Bars.Count - 1;

										// Repaint the column on the SuperDOM
										OnPropertyChanged();
									}
								}
								catch 
								{
									if (State != State.Finalized)  // ignore error if Finalized
										throw;
								}

								if (SuperDom != null && !clearLoadingSent)
								{
									SuperDom.Dispatcher.InvokeAsync(() => SuperDom.ClearLoadingString());
									clearLoadingSent = true;
								}
							}
						});
				}
			}
			else if (State == State.Active)
			{
				if (!DisplayText)
				{
					WeakEventManager<System.Windows.Controls.Panel, MouseEventArgs>.AddHandler(UiWrapper, "MouseMove", OnMouseMove);
					WeakEventManager<System.Windows.Controls.Panel, MouseEventArgs>.AddHandler(UiWrapper, "MouseEnter", OnMouseEnter);
					WeakEventManager<System.Windows.Controls.Panel, MouseEventArgs>.AddHandler(UiWrapper, "MouseLeave", OnMouseLeave);
					mouseEventsSubscribed = true;
				}
			}
			else if (State == State.Terminated)
			{
				if (BarsRequest != null)
				{
					BarsRequest.Update -= OnBarsUpdate;
					BarsRequest.Dispose();
				}

				BarsRequest = null;

				if (SuperDom != null && !clearLoadingSent)
				{
					SuperDom.Dispatcher.InvokeAsync(() => SuperDom.ClearLoadingString());
					clearLoadingSent = true;
				}

				if (!DisplayText && mouseEventsSubscribed)
				{
					WeakEventManager<System.Windows.Controls.Panel, MouseEventArgs>.RemoveHandler(UiWrapper, "MouseMove", OnMouseMove);
					WeakEventManager<System.Windows.Controls.Panel, MouseEventArgs>.RemoveHandler(UiWrapper, "MouseEnter", OnMouseEnter);
					WeakEventManager<System.Windows.Controls.Panel, MouseEventArgs>.RemoveHandler(UiWrapper, "MouseLeave", OnMouseLeave);
					mouseEventsSubscribed = false;
				}

				lastMaxIndex	= 0;
				maxVolume		= 0;
				totalBuyVolume	= 0;
				totalLastVolume = 0;
				totalSellVolume = 0;
				Sells.Clear();
				Buys.Clear();
				LastVolumes.Clear();
			}
		}

		#region Bar Collections
		[XmlIgnore]
		[Browsable(false)]
		public ConcurrentDictionary<double, long> Buys { get; set; }

		[XmlIgnore]
		[Browsable(false)]
		public ConcurrentDictionary<double, long> LastVolumes { get; set; }

		[XmlIgnore]
		[Browsable(false)]
		public ConcurrentDictionary<double, long> Sells { get; set; }
		#endregion

		#region Properties
		[XmlIgnore]
		[Display(ResourceType = typeof(Resource), Name = "NinjaScriptColumnBaseBackground", GroupName = "PropertyCategoryVisual", Order = 130)]
		public Brush BackColor { get; set; }

		[Browsable(false)]
		public string BackColorSerialize
		{
			get { return NinjaTrader.Gui.Serialize.BrushToString(BackColor, "brushPriceColumnBackground"); }
			set { BackColor = NinjaTrader.Gui.Serialize.StringToBrush(value, "brushPriceColumnBackground"); }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Resource), Name = "NinjaScriptBarColor", GroupName = "PropertyCategoryVisual", Order = 110)]
		public Brush BarColor { get; set; }

		[Browsable(false)]
		public string BarColorSerialize
		{
			get { return NinjaTrader.Gui.Serialize.BrushToString(BarColor); }
			set { BarColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Resource), Name = "NinjaScriptBuyColor", GroupName = "PropertyCategoryVisual", Order = 120)]
		public Brush BuyColor { get; set; }

		[Browsable(false)]
		public string BuyColorSerialize
		{
			get { return NinjaTrader.Gui.Serialize.BrushToString(BuyColor); }
			set { BuyColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
		}

		[Display(ResourceType = typeof(Resource), Name = "NinjaScriptDisplayText", GroupName = "PropertyCategoryVisual", Order = 175)]
		public bool DisplayText { get; set; }

		[Display(ResourceType = typeof(Resource), Name = "NinjaScriptDisplayType", GroupName = "NinjaScriptSetup", Order = 150)]
		public DisplayType DisplayType { get; set; }

		[XmlIgnore]
		[Display(ResourceType = typeof(Resource), Name = "NinjaScriptColumnBaseForeground", GroupName = "PropertyCategoryVisual", Order = 140)]
		public Brush ForeColor { get; set; }

		[Browsable(false)]
		public string ForeColorSerialize
		{
			get { return NinjaTrader.Gui.Serialize.BrushToString(ForeColor, "brushVolumeColumnForeground"); }
			set { ForeColor = NinjaTrader.Gui.Serialize.StringToBrush(value, "brushVolumeColumnForeground"); }
		}

		[XmlIgnore]
		[Browsable(false)]
		public Brush ImmutableBarColor { get; set; }

		[Browsable(false)]
		public string ImmutableBarColorSerialize
		{
			get { return NinjaTrader.Gui.Serialize.BrushToString(ImmutableBarColor, "CustomVolume.ImmutableBarColor"); }
			set { ImmutableBarColor = NinjaTrader.Gui.Serialize.StringToBrush(value, "CustomVolume.ImmutableBarColor"); }
		}

		[XmlIgnore]
		[Browsable(false)]
		public Brush ImmutableForeColor { get; set; }

		[Browsable(false)]
		public string ImmutableForeColorSerialize
		{
			get { return NinjaTrader.Gui.Serialize.BrushToString(ImmutableForeColor, "CustomVolume.ImmutableForeColor"); }
			set { ImmutableForeColor = NinjaTrader.Gui.Serialize.StringToBrush(value, "CustomVolume.ImmutableForeColor"); }
		}

		[XmlIgnore]
		[Display(ResourceType = typeof(Resource), Name = "NinjaScriptSellColor", GroupName = "PropertyCategoryVisual", Order = 170)]
		public Brush SellColor { get; set; }

		[Browsable(false)]
		public string SellColorSerialize
		{
			get { return NinjaTrader.Gui.Serialize.BrushToString(SellColor); }
			set { SellColor = NinjaTrader.Gui.Serialize.StringToBrush(value); }
		}

		[Display(ResourceType = typeof(Resource), Name = "IndicatorSuperDomBaseTradingHoursTemplate", GroupName = "NinjaScriptTimeFrame", Order = 60)]
		[Gui.PropertyEditor("NinjaTrader.Gui.Tools.StringStandardValuesEditorKey")]
		[RefreshProperties(RefreshProperties.All)]
		[TypeConverter(typeof(NinjaTrader.NinjaScript.TradingHoursDataConverter))]
		[XmlIgnore]
		public Data.TradingHours TradingHoursInstance
		{
			get
			{
				if (TradingHoursSerializable.Length > 0)
				{
					Data.TradingHours result = Data.TradingHours.All.FirstOrDefault(t => t.Name == TradingHoursSerializable);
						if (result != null)
							return result;
				}
				return Data.TradingHours.UseInstrumentSettingsInstance;  // return default in case not found (e.g. legacy UseInstrumentSettings in TradingHoursTemplate)
			}
			set { TradingHoursSerializable = (value == Data.TradingHours.UseInstrumentSettingsInstance ? string.Empty : value.Name); }
		}

		[Browsable(false)]
		public string TradingHoursSerializable
		{
			get { return tradingHoursSerializable; }
			set { tradingHoursSerializable = value; }
		}

		[Display(ResourceType = typeof(Resource), Name = "GuiType", GroupName = "NinjaScriptSetup", Order = 180)]
		public VolumeType VolumeType { get; set; }
		#endregion
	}

	public enum VolumeType
	{
		BuySell,
		Standard
	}

	public enum DisplayType
	{
		Percent,
		Volume
	}
}
