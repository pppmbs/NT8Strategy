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
	public class ProfitChasingReversal : Strategy
	{
		#region Variables
		private double currentPtPrice, currentSlPrice, tickSizeSecondary;
		private Order entryOrder, exitFlat, exitSession;
		private Order placeHolderOrder, profitTarget, stopLoss;
		private string exitName, message;
		private bool exitOnCloseWait, ordersCancelled, suppressOco;
		private SessionIterator sessionIterator;

		private double lastRSI = 0.0;

		private readonly double rsiUpperBound = 80;
		private readonly double rsiLowerBound = 20;


		private bool rsiLongOppornuity = false;
		private bool rsiShortOppornuity = false;
		private bool isLongTrade = false;

		private int profiltsTaking = 24; // number of ticks for profits taking
		private int stopLossVal = 6; // number of ticks for stop loss
		private readonly int maxConsecutiveLosingTrades = 3;
		private readonly int TargetProfitsNumber = 2;

		private int lastProfitableTrades = 0;    // This variable holds our value for how profitable the last three trades were.
		private int priorNumberOfTrades = 0;    // This variable holds the number of trades taken. It will be checked every OnBarUpdate() to determine when a trade has closed.
		private int priorSessionTrades = 0; // This variable holds the number of trades taken prior to each session break.
		#endregion

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = @"Reversal with profit chasing characteristic";
				Name = "ProfitChaseStopTrailExitOrdersExample";
				Calculate = Calculate.OnBarClose;
				EntriesPerDirection = 1;
				EntryHandling = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = true;
				ExitOnSessionCloseSeconds = 30;
				IsFillLimitOnTouch = false;
				TraceOrders = false;
				BarsRequiredToTrade = 1;
				IsInstantiatedOnEachOptimizationIteration = false;

				ChaseProfitTarget = true;
				PrintDetails = false;
				ProfitTargetDistance = profiltsTaking;
				StopLossDistance = stopLossVal;
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
				message = string.Empty;
				currentPtPrice = 0;
				currentSlPrice = 0;
				exitOnCloseWait = true;
				placeHolderOrder = new Order();
				suppressOco = false;
				tickSizeSecondary = BarsArray[1].Instrument.MasterInstrument.TickSize;
			}
		}

		protected void TradeAccounting()
		{
			/* Here, SystemPerformance.AllTrades.Count - priorSessionTrades checks to make sure there have been three trades today.
            priorNumberOfTrades makes sure this code block only executes when a new trade has finished. */
			if ((SystemPerformance.AllTrades.Count - priorSessionTrades) >= 3 && SystemPerformance.AllTrades.Count != priorNumberOfTrades)
			{
				// Reset the counter.
				lastProfitableTrades = 0;

				// Set the new number of completed trades.
				priorNumberOfTrades = SystemPerformance.AllTrades.Count;
				// Loop through the last three trades and check profit/loss on each.
				for (int idx = 1; idx <= maxConsecutiveLosingTrades; idx++)
				{
					/* The SystemPerformance.AllTrades array stores the most recent trade at the highest index value. If there are a total of 10 trades,
                       this loop will retrieve the 10th trade first (at index position 9), then the 9th trade (at 8), then the 8th trade. */
					Trade trade = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - idx];

					/* If the trade's profit is greater than 0, add one to the counter. If the trade's profit is less than 0, subtract one.
                        This logic means break-even trades have no effect on the counter. */
					if (trade.ProfitCurrency > 0)
					{
						lastProfitableTrades++;
					}

					else if (trade.ProfitCurrency < 0)
					{
						lastProfitableTrades--;
					}
				}
			}
		}

		protected bool NoConsecutiveLosingTrades()
		{
			return (lastProfitableTrades != -maxConsecutiveLosingTrades);
		}

		protected bool AchievedDailyProfitsGoal()
		{
			return (lastProfitableTrades >= TargetProfitsNumber);
		}

		protected bool NoActiveTrade()
		{
			//return (entryOrder == null && Position.MarketPosition == MarketPosition.Flat);
			return (entryOrder == null);
		}

		protected bool IsUpTrend()
		{
			return (DM(14).DiPlus[0] > DM(14).DiMinus[0]);
		}

		protected bool PriceActionHasMomentum(double m)
		{
			return (ADX(14)[0] > m);
		}

		protected void CheckforRsiOpportunity()
		{
			if (CrossAbove(RSI(14, 3), rsiUpperBound, 1))
			{
				rsiShortOppornuity = true;
				lastRSI = RSI(14, 3)[0];
			}
			else if (CrossBelow(RSI(14, 3), rsiLowerBound, 1))
			{
				rsiLongOppornuity = true;
				lastRSI = RSI(14, 3)[0];
			}
		}

		protected void ReversalTrade()
		{
			TradeAccounting();

			/* If lastProfitableTrades = -consecutiveLosingTrades, that means the last consecutive trades were all losing trades.
                Don't take anymore trades if this is the case. This counter resets every new session, so it only stops trading for the current day. */
			if (NoConsecutiveLosingTrades())
			{
				// Submit an entry market order if we currently don't have an entry order open and are past the BarsRequiredToTrade bars amount
				if (NoActiveTrade())
				{

					CheckforRsiOpportunity();

					if (rsiLongOppornuity)
					{
						if (RSI(14, 3)[0] <= lastRSI)
						{
							lastRSI = RSI(14, 3)[0];
						}
						else
						{
							//if (PriceActionHasMomentum(40) && (CrossBelow(SMA(9), SMA(20), 10) || CrossAbove(SMA(9), SMA(20), 10)))
							if (PriceActionHasMomentum(40))
							{
								profiltsTaking = 24;
								stopLossVal = 6;
								isLongTrade = true;
								EnterLong(1, 1, "entry");
								//EnterLongLimit(1, Close[0], "Long");
							}
							rsiLongOppornuity = false;
						}
					}
					else if (rsiShortOppornuity)
					{
						if (RSI(14, 3)[0] >= lastRSI)
						{
							lastRSI = RSI(14, 3)[0];
						}
						else
						{
							//if (PriceActionHasMomentum(40) && (CrossBelow(SMA(9), SMA(20), 10) || CrossAbove(SMA(9), SMA(20), 10)))
							if (PriceActionHasMomentum(40))
							{
								profiltsTaking = 24;
								stopLossVal = 6;
								isLongTrade = false;
								EnterShort(1, 1, "entry");
								//EnterShortLimit(1, High[0], "Short");
							}
							rsiShortOppornuity = false;
						}
					}
				}
			}
		}

		protected void ExitToStartFlat()
        {
			suppressOco = true;
			exitName = "exit to start flat";
			exitFlat = placeHolderOrder;

			if (isLongTrade)
            {
				ExitLong(1, entryOrder.Quantity, exitName, entryOrder.Name);

			}
			else
            {
				ExitShort(1, entryOrder.Quantity, exitName, entryOrder.Name);
			}
		}

		protected void ExitForCancel()
        {
			message += "; exiting and resetting";
			if (PrintDetails)
				Print(message);

			exitName = "exit for cancel";
			exitFlat = placeHolderOrder;

			if (isLongTrade)
			{
				ExitLong(exitName, entryOrder.Name);
			}
            else
            {
				ExitShort(exitName, entryOrder.Name);
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
						ExitToStartFlat();
					}
				}
				// if this is not the last historical bar, and entryOrder is null, then place an entry order
				else if (!exitOnCloseWait && entryOrder == null && profitTarget == null && stopLoss == null)
				{
					suppressOco = false;
					entryOrder = placeHolderOrder;
					ReversalTrade();
				}
			}

			// all code below this point takes places during BarsInProgress 1 when the tick series is processing
			if (BarsInProgress != 1)
				return;

			if (ordersCancelled)
			{
				message = string.Format("{0} | OBU | stop and/or target cancelled or rejected", Times[1][0]);

				// if the orders were cancelled due to the exit on close, do not submit an order to flatten
				if (!exitOnCloseWait && entryOrder != null && entryOrder.OrderState == OrderState.Filled)
				{
					ExitForCancel();
				}

				if (entryOrder == null || entryOrder.OrderState != OrderState.Filled)
					Print(string.Format("{0} | OBU | entry not filled or is null", Times[1][0]));

				ordersCancelled = false;
				return;
			}

			// trailing logic
			// the profitTarget/stopLoss is first created when the entry order fills. If it exists then move it.

			// trigger the chase action when the current price is further than the set distance to the profit target
			if (ChaseProfitTarget &&
				profitTarget != null && (profitTarget.OrderState == OrderState.Accepted || profitTarget.OrderState == OrderState.Working) &&
				Close[0] < currentPtPrice - ProfitTargetDistance * tickSizeSecondary)
			{
				// setting profit target
				if (isLongTrade)
                {
					currentPtPrice = Close[0] + ProfitTargetDistance * tickSizeSecondary;
					ExitLongLimit(1, true, entryOrder.Quantity, currentPtPrice, "profit target", entryOrder.Name);
				}
				else
                {
					currentPtPrice = Close[0] - ProfitTargetDistance * tickSizeSecondary;
					ExitLongLimit(1, true, entryOrder.Quantity, currentPtPrice, "profit target", entryOrder.Name);
				}
			}

			// trigger the trail action when the current price is further than the set distance to the stop loss
			if (TrailStopLoss &&
				stopLoss != null && (stopLoss.OrderState == OrderState.Accepted || stopLoss.OrderState == OrderState.Working) &&
				Close[0] > currentSlPrice + StopLossDistance * tickSizeSecondary)
			{
				// setting stop loss
				if (isLongTrade)
				{
					currentSlPrice = Close[0] - StopLossDistance * tickSizeSecondary;
					ExitLongStopMarket(1, true, entryOrder.Quantity, currentSlPrice, "stop loss", entryOrder.Name);
				}
				else
				{
					currentSlPrice = Close[0] + StopLossDistance * tickSizeSecondary;
					ExitLongStopMarket(1, true, entryOrder.Quantity, currentSlPrice, "stop loss", entryOrder.Name);
				}
			}
		}

		protected override void OnExecutionUpdate(Cbi.Execution execution, string executionId, double price, int quantity,
			Cbi.MarketPosition marketPosition, string orderId, DateTime time)
		{
			if (PrintDetails)
				Print(string.Format("{0} | OEU | execution | {1} | {2}", Times[1][0], time, execution.ToString()));

			if (execution.Order.OrderState != OrderState.Filled)
				return;

			// when the entry order fills, place the profit target and stop loss
			if (entryOrder != null && execution.Order == entryOrder)
			{
				if (UseProfitTarget)
				{
					if (PrintDetails)
						Print(string.Format("{0} | OEU | placing profit target", execution.Time));

					// setting profit target
					if (isLongTrade)
                    {
						// calculate  a price for the profit target using the secondary series ticksize
						currentPtPrice = execution.Order.AverageFillPrice + ProfitTargetDistance * tickSizeSecondary;
						profitTarget = placeHolderOrder;
						ExitLongLimit(1, true, entryOrder.Quantity, currentPtPrice, "profit target", "entry");
					}
					else
                    {
						// calculate  a price for the profit target using the secondary series ticksize
						currentPtPrice = execution.Order.AverageFillPrice - ProfitTargetDistance * tickSizeSecondary;
						profitTarget = placeHolderOrder;
						ExitShortLimit(1, true, entryOrder.Quantity, currentPtPrice, "profit target", "entry");
					}
				}

				if (UseStopLoss)
				{
					if (PrintDetails)
						Print(string.Format("{0} | OEU | placing stop loss", execution.Time));

					//setting stop loss
					if (isLongTrade)
					{
						currentSlPrice = execution.Order.AverageFillPrice - StopLossDistance * tickSizeSecondary;
						stopLoss = placeHolderOrder;
						ExitLongStopMarket(1, true, entryOrder.Quantity, currentSlPrice, "stop loss", "entry");
					}
					else
					{
						currentSlPrice = execution.Order.AverageFillPrice + StopLossDistance * tickSizeSecondary;
						stopLoss = placeHolderOrder;
						ExitShortStopMarket(1, true, entryOrder.Quantity, currentSlPrice, "stop loss", "entry");
					}
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

			// the logic below is to simulate OCO when manually cancelling an order.

			// if the stop or target has filled (instead of being cancelled), nt will automatically
			// cancel any remaining orders attached to the entry's signal name.
			// prevent the OCO logic below to prevent overfills (cancelling an already cancelled order) 
			if (orderState == OrderState.Filled && ((profitTarget != null && order == profitTarget) ||
				(stopLoss != null && order == stopLoss)))
			{
				if (PrintDetails)
					Print(string.Format("{0} | OOU | stop or target filled, preventing oco actions until reset", Times[1][0]));

				suppressOco = true;
			}

			// oco strings cannot be used in the managed approach. So simulate OCO here for manually cancelled orders
			// if the profit target is cancelled and the stop loss is still working, cancel the stop loss
			if (!suppressOco && profitTarget != null && order == profitTarget &&
				(orderState == OrderState.Cancelled || orderState == OrderState.Rejected) &&
				stopLoss != null &&
				(stopLoss.OrderState == OrderState.Accepted || stopLoss.OrderState == OrderState.Working))
			{
				if (PrintDetails)
					Print(string.Format("{0} | OOU | cancelling stop", Times[1][0]));

				CancelOrder(stopLoss);
			}

			// if the stop loss is cancelled and the profit target is still working, cancel the profit target
			else if (!suppressOco && stopLoss != null && order == stopLoss &&
					(orderState == OrderState.Cancelled || orderState == OrderState.Rejected) &&
					profitTarget != null &&
					(profitTarget.OrderState == OrderState.Accepted ||
						profitTarget.OrderState == OrderState.Working))
			{
				if (PrintDetails)
					Print(string.Format("{0} | OOU | cancelling target", Times[1][0]));

				CancelOrder(profitTarget);
			}

			// if both the profit target and stop loss are cancelled or rejected, 
			// then exit the position and reset for a new entry
			else if (entryOrder != null && order != entryOrder &&
					(!UseProfitTarget || (profitTarget != null &&
							(profitTarget.OrderState == OrderState.Cancelled ||
								profitTarget.OrderState == OrderState.Rejected))) &&
					(!UseStopLoss || (stopLoss != null &&
							(stopLoss.OrderState == OrderState.Cancelled ||
								stopLoss.OrderState == OrderState.Rejected))))
			{
				// if either the stop or target is cancelled, wait 1 tick in OBU to check
				// to see if this was because of the Exit on close occurring or if manually cancelled
				ordersCancelled = true;

				if (PrintDetails)
					Print(string.Format("{0} | OOU | stop or target cancelled or rejected", Times[1][0]));
			}

			// end of simulated oco logic for manual cancellations

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
