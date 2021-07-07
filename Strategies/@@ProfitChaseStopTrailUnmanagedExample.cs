#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
	public class ProfitChaseStopTrailUnmanagedExample : Strategy
	{
		#region Variables
		private double currentPtPrice, currentSlPrice, tickSizeSecondary;
		private Order entryOrder, exitFlat, exitSession;
		private Order placeHolderOrder, profitTarget, stopLoss;
		private bool exitOnCloseWait, ordersCancelled, suppressCancelExit;
		private string entryName, exitName, ptName, slName, message, ocoString;
		private SessionIterator sessionIterator;

		private int tradeCount = 0;
		#endregion

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = @"Unmanaged Order Example";
				Name = "ProfitChaseStopTrailUnmanagedExample";
				Calculate = Calculate.OnBarClose;
				EntriesPerDirection = 1;
				EntryHandling = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = true;
				ExitOnSessionCloseSeconds = 30;
				IsFillLimitOnTouch = false;
				TraceOrders = true;
				BarsRequiredToTrade = 1;
				IsInstantiatedOnEachOptimizationIteration = false;
				IsUnmanaged = true;

				ChaseProfitTarget = true;
				PrintDetails = false;
				ProfitTargetDistance = 10;
				StopLossDistance = 10;
				TrailStopLoss = true;
				UseProfitTarget = true;
				UseStopLoss = true;
			}
			else if (State == State.Configure)
			{
				TraceOrders = PrintDetails;

				AddDataSeries(BarsPeriodType.Tick, 1);
			}
			else if (State == State.Historical)
			{
				if (PrintDetails)
					ClearOutputWindow();

				sessionIterator = new SessionIterator(Bars);
				currentPtPrice = 0;
				currentSlPrice = 0;
				entryName = string.Empty;
				exitName = string.Empty;
				message = string.Empty;
				ptName = string.Empty;
				slName = string.Empty;
				exitOnCloseWait = false;
				suppressCancelExit = false;
				placeHolderOrder = new Order();
				tickSizeSecondary = BarsArray[1].Instrument.MasterInstrument.TickSize;
			}
		}

		private void AssignOrderToVariable(ref Order order)
		{
			// Assign Order variable in OnOrderUpdate() to ensure the assignment occurs when expected.
			// This is more reliable than assigning Order objects in OnBarUpdate, as the assignment is not guaranteed to be complete if it is referenced immediately after submitting

			//if (PrintDetails)
			//	Print(string.Format("{0} | assigning {1} to variable", Times[1][0], order.Name));

			if (order.Name == "entry" && entryOrder != order)
				entryOrder = order;

			if (order.Name == "profit target" && profitTarget != order)
				profitTarget = order;

			if (order.Name == "stop loss" && stopLoss != order)
				stopLoss = order;

			if (order.Name == exitName && exitFlat != order)
				exitFlat = order;

			if (order.Name == "Exit on session close" && exitSession != order)
				exitSession = order;
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBars[0] < BarsRequiredToTrade || CurrentBars[1] < BarsRequiredToTrade)
				return;

			if (BarsInProgress == 0)
				sessionIterator.GetNextSession(Time[0], true);

			// if after the exit on close, prevent new orders until the new session
			if (Times[1][0] >= sessionIterator.ActualSessionEnd.AddSeconds(-ExitOnSessionCloseSeconds) && Times[1][0] <= sessionIterator.ActualSessionEnd)
			{
				exitOnCloseWait = true;
			}

			// an exit on close occurred in the previous session, reset for a new entry on the first bar of a new session
			if (exitOnCloseWait && Bars.IsFirstBarOfSession)
			{
				entryOrder = null;
				profitTarget = null;
				stopLoss = null;
				exitFlat = null;
				exitSession = null;
				exitOnCloseWait = false;
				suppressCancelExit = false;
			}

			// the entry logic can be done when the primary series is processing
			if (BarsInProgress == 0)
			{
				// because this is a demonstration, this code will cause any historical position
				// to be exited on the last historical bar so the strategy will always start flat in real-time
				if (State == State.Historical && CurrentBar == BarsArray[0].Count - 2)
				{
					if (entryOrder != null)
					{
						if (profitTarget != null && (profitTarget.OrderState == OrderState.Accepted || profitTarget.OrderState == OrderState.Working))
							CancelOrder(profitTarget);

						else if (stopLoss != null && (stopLoss.OrderState == OrderState.Accepted || stopLoss.OrderState == OrderState.Working))
							CancelOrder(stopLoss);

						ordersCancelled = false;
						suppressCancelExit = true;

						exitName = "exit to start flat";
						exitFlat = placeHolderOrder;
						SubmitOrderUnmanaged(1, OrderAction.Sell, OrderType.Market, 1, 0, 0, string.Empty, exitName);
					}
				}
				// if this is not the last historical bar, and entryOrder is null, then place an entry order
				else if (!exitOnCloseWait && entryOrder == null && profitTarget == null && stopLoss == null)
				{
					Print(string.Format("ProfitChaseStopTrailUnmanagedExample:: tradeCount {0}", tradeCount++));

					entryName = "entry";
					entryOrder = placeHolderOrder;
					SubmitOrderUnmanaged(1, OrderAction.Buy, OrderType.Market, 1, 0, 0, string.Empty, entryName);
				}
			}

			// all code below this point takes places during BarsInProgress 1 when the secondary series is processing
			if (BarsInProgress != 1)
				return;

			if (ordersCancelled)
			{
				message = string.Format("{0} stop and/or target cancelled or rejected", Times[1][0]);

				if (entryOrder == null || entryOrder.OrderState != OrderState.Filled && PrintDetails)
					Print(string.Format("{0} | OBU | entry not filled or is null", Times[1][0]));

				// if the orders were cancelled due to the exit on close, do not submit an order to flatten
				if (!exitOnCloseWait && !suppressCancelExit && entryOrder != null && entryOrder.OrderState == OrderState.Filled)
				{
					message += "; exiting and resetting";
					if (PrintDetails)
						Print(message);

					exitName = "exit for cancel";
					exitFlat = placeHolderOrder;
					SubmitOrderUnmanaged(1, OrderAction.Sell, OrderType.Market, entryOrder.Filled, 0, 0, string.Empty, exitName);
				}

				ordersCancelled = false;
				return;
			}

			// the profitTarget/stopLoss is first created when the entry order fills. If it exists then move it.

			// trigger the chase action when the current price is further than the set distance to the profit target
			if (ChaseProfitTarget &&
				profitTarget != null && (profitTarget.OrderState == OrderState.Accepted || profitTarget.OrderState == OrderState.Working) &&
				Close[0] < currentPtPrice - ProfitTargetDistance * tickSizeSecondary)
			{
				currentPtPrice = Close[0] + ProfitTargetDistance * tickSizeSecondary;
				ChangeOrder(profitTarget, entryOrder.Filled, currentPtPrice, 0);
			}

			// trigger the trail action when the current price is further than the set distance to the stop loss
			if (TrailStopLoss &&
				stopLoss != null && (stopLoss.OrderState == OrderState.Accepted || stopLoss.OrderState == OrderState.Working) &&
				Close[0] > currentSlPrice + StopLossDistance * tickSizeSecondary)
			{
				currentSlPrice = Close[0] - ProfitTargetDistance * tickSizeSecondary;
				ChangeOrder(stopLoss, entryOrder.Filled, 0, currentSlPrice);
			}
		}

		protected override void OnExecutionUpdate(Cbi.Execution execution, string executionId, double price, int quantity,
			Cbi.MarketPosition marketPosition, string orderId, DateTime time)
		{
			if (PrintDetails)
				Print(string.Format("{0} | OEU | execution | {1} | {2}", Times[1][0], time, execution.ToString()));

			if (execution.Order.OrderState != OrderState.Filled)
				return;

			// when the entry order fully fills, place the profit target and stop loss
			if (entryOrder != null && execution.Order == entryOrder)
			{
				ocoString = Guid.NewGuid().ToString();

				if (UseProfitTarget)
				{
					if (PrintDetails)
						Print(string.Format("{0} | OEU | placing profit target", execution.Time));

					// calculate  a price for the profit target using the secondary series ticksize
					currentPtPrice = execution.Order.AverageFillPrice + ProfitTargetDistance * tickSizeSecondary;
					profitTarget = placeHolderOrder;
					SubmitOrderUnmanaged(1, OrderAction.Sell, OrderType.Limit, execution.Order.Filled, currentPtPrice, 0, ocoString, "profit target");
				}

				if (UseStopLoss)
				{
					if (PrintDetails)
						Print(string.Format("{0} | OEU | placing stop loss", execution.Time));

					currentSlPrice = execution.Order.AverageFillPrice - StopLossDistance * tickSizeSecondary;
					stopLoss = placeHolderOrder;
					SubmitOrderUnmanaged(1, OrderAction.Sell, OrderType.StopMarket, execution.Order.Filled, 0, currentSlPrice, ocoString, "stop loss");
				}
			}
		}

		protected override void OnOrderUpdate(Cbi.Order order, double limitPrice, double stopPrice,
			int quantity, int filled, double averageFillPrice,
			Cbi.OrderState orderState, DateTime time, Cbi.ErrorCode error, string comment)
		{
			if (PrintDetails)
				Print(string.Format("{0} | OOU | order | {1} | {2}", Times[1][0], time, order.ToString()));

			AssignOrderToVariable(ref order);

			// if either the profit target or stop loss is cancelled or rejected, then reset for a new entry
			if (!suppressCancelExit && entryOrder != null && order != entryOrder &&
				(!UseProfitTarget || (profitTarget != null &&
						(profitTarget.OrderState == OrderState.Cancelled ||
							profitTarget.OrderState == OrderState.Rejected))) &&
				(!UseStopLoss || (stopLoss != null &&
						(stopLoss.OrderState == OrderState.Cancelled ||
							stopLoss.OrderState == OrderState.Rejected)))
				)
			{
				// if either the stop or target is cancelled, wait 1 tick in OBU to check
				// to see if this was because of the Exit on close occurring or if manually cancelled
				ordersCancelled = true;

				if (PrintDetails)
					Print(string.Format("{0} stop or target cancelled or rejected", Times[1][0]));
			}

			// once the stop loss or profit target (or exit for flat / exit for manual cancel) fills, reset for a new entry
			if ((profitTarget != null && profitTarget.OrderState == OrderState.Filled && (stopLoss == null || stopLoss.OrderState == OrderState.Cancelled)) ||
				(stopLoss != null && stopLoss.OrderState == OrderState.Filled && (profitTarget == null || profitTarget.OrderState == OrderState.Cancelled)) ||
				(exitFlat != null && exitFlat.OrderState == OrderState.Filled && (profitTarget == null || profitTarget.OrderState == OrderState.Cancelled) && (stopLoss == null || stopLoss.OrderState == OrderState.Cancelled)))
			{
				if (PrintDetails)
					Print(string.Format("{0} | OOU | resetting", Times[1][0]));

				entryOrder = null;
				profitTarget = null;
				stopLoss = null;
				exitFlat = null;
				suppressCancelExit = false;
			}

			// when the Exit on close fills, wait for the new session to start the next entry
			// (so no orders are submitted between the exit on close and the end of the session)
			if (exitSession != null && exitSession.OrderState == OrderState.Filled)
			{
				if (PrintDetails)
					Print(string.Format("{0} | OOU | exit on close filled waiting for next session for reset\r\n{1}", Times[1][0], order.ToString()));

				exitOnCloseWait = true;
			}
		}

		#region Properties
		[NinjaScriptProperty]
		[Display(Name = "Chase profit target", Order = 2, GroupName = "NinjaScriptStrategyParameters")]
		public bool ChaseProfitTarget
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Profit target distance", Description = "Distance for profit target (in ticks)", Order = 3, GroupName = "NinjaScriptStrategyParameters")]
		public int ProfitTargetDistance
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Print details", Order = 7, GroupName = "NinjaScriptStrategyParameters")]
		public bool PrintDetails
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Stop loss distance", Description = "Distance for stop loss (in ticks)", Order = 6, GroupName = "NinjaScriptStrategyParameters")]
		public int StopLossDistance
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Trail stop loss", Order = 5, GroupName = "NinjaScriptStrategyParameters")]
		public bool TrailStopLoss
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use profit target", Order = 1, GroupName = "NinjaScriptStrategyParameters")]
		public bool UseProfitTarget
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use stop loss", Order = 4, GroupName = "NinjaScriptStrategyParameters")]
		public bool UseStopLoss
		{ get; set; }
		#endregion

	}
}
