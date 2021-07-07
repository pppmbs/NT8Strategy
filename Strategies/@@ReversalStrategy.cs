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
    public class ReversalStrategy : Strategy
    {
        private int Fast;
        private int Slow;

        private int tradeCount = 0;

        private double lastRSI = 0.0;
        private Order entryOrder = null; // This variable holds an object representing our entry order
        private Order stopOrder = null; // This variable holds an object representing our stop loss order
        private Order targetOrder = null; // This variable holds an object representing our profit target order
        private int sumFilled = 0; // This variable tracks the quantities of each execution making up the entry order

        private readonly double rsiUpperBound = 80;
        private readonly double rsiLowerBound = 20;

        private bool rsiLongOppornuity = false;
        private bool rsiShortOppornuity = false;

        private int profiltsTaking = 24; // number of ticks for profits taking
        private int stopLoss = 6; // number of ticks for stop loss
        private readonly int maxConsecutiveLosingTrades = 3;
        private readonly int TargetProfitsNumber = 2;
        private int targetIncrement = 0;
        private int stopLossIncrement = 0;

        private int lastProfitableTrades = 0;    // This variable holds our value for how profitable the last three trades were.
        private int priorNumberOfTrades = 0;    // This variable holds the number of trades taken. It will be checked every OnBarUpdate() to determine when a trade has closed.
        private int priorSessionTrades = 0; // This variable holds the number of trades taken prior to each session break.

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Enter the description for your new custom Strategy here.";
                Name = "ReversalStrategy";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.ByStrategyPosition;
                BarsRequiredToTrade = 20;
                Fast = 9;
                Slow = 20;
            }
            else if (State == State.Configure)
            {
                /* Add a secondary bar series. 
				Very Important: This secondary bar series needs to be smaller than the primary bar series.
				
				Note: The primary bar series is whatever you choose for the strategy at startup. In this example I will
				reference the primary as a 5min bars series. */
                AddDataSeries(Data.BarsPeriodType.Tick, 1);

                // Add two EMA indicators to be plotted on the primary bar series
                AddChartIndicator(EMA(Fast));
                AddChartIndicator(EMA(Slow));

                /* Adjust the color of the EMA plots.
				For more information on this please see this tip: http://www.ninjatrader-support.com/vb/showthread.php?t=3228 */
                EMA(Fast).Plots[0].Brush = Brushes.Blue;
                EMA(Slow).Plots[0].Brush = Brushes.Green;
            }
            else if (State == State.Realtime)
            {
                // one time only, as we transition from historical
                // convert any old historical order object references
                // to the new live order submitted to the real-time account
                if (entryOrder != null)
                    entryOrder = GetRealtimeOrder(entryOrder);
                if (stopOrder != null)
                    stopOrder = GetRealtimeOrder(stopOrder);
                if (targetOrder != null)
                    targetOrder = GetRealtimeOrder(targetOrder);
            }
        }

        protected void InitializeTradeAccounting()
        {
            lastProfitableTrades = 0;
            priorSessionTrades = SystemPerformance.AllTrades.Count;
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
            Print(string.Format("ReversalStrategy:: tradeCount {0}", tradeCount++)); 

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
                                stopLoss = 6;
                                EnterLong(1, 1, "Long");
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
                                stopLoss = 6;
                                EnterShort(1, 1, "Short");
                                //EnterShortLimit(1, High[0], "Short");
                            }
                            rsiShortOppornuity = false;
                        }
                    }
                }
            }
        }

        protected void AdjustTargetStopLoss()
        {

            if (entryOrder == null)
            {
                stopLossIncrement = 0;
                targetIncrement = 0;
                return;
            }

            // If a long position is open, allow for stop loss modification to breakeven
            if (Position.MarketPosition == MarketPosition.Long)
            {
                // Once the price is greater than target, set stop loss to breakeven
                if (Close[0] >= (Position.AveragePrice + profiltsTaking + targetIncrement))
                {
                    SetStopLoss(CalculationMode.Price, Position.AveragePrice + stopLossIncrement);
                    SetProfitTarget(CalculationMode.Price, Position.AveragePrice + profiltsTaking + targetIncrement + 4);

                    stopLossIncrement += 4;
                    targetIncrement += 4;
                }
            }
            else
            {
                // Once the price is greater than target, set stop loss to breakeven
                if (Close[0] <= (Position.AveragePrice - profiltsTaking - targetIncrement))
                {
                    SetStopLoss(CalculationMode.Price, Position.AveragePrice - stopLossIncrement);
                    SetProfitTarget(CalculationMode.Price, Position.AveragePrice - profiltsTaking - targetIncrement - 4);

                    stopLossIncrement -= 4;
                    targetIncrement -= 4;
                }

            }
        }

        protected override void OnBarUpdate()
        {
            /* When working with multiple bar series objects it is important to understand the sequential order in which the
            OnBarUpdate() method is triggered. The bars will always run with the primary first followed by the secondary and
            so on.

            Important: Primary bars will always execute before the secondary bar series.
            If a bar is timestamped as 12:00PM on the 5min bar series, the call order between the equally timestamped 12:00PM
            bar on the 1min bar series is like this:
                12:00PM 5min
                12:00PM 1min
                12:01PM 1min
                12:02PM 1min
                12:03PM 1min
                12:04PM 1min
                12:05PM 5min
                12:05PM 1min 

            When the OnBarUpdate() is called from the primary bar series (2000 ticks series in this example), do the following */
            if (BarsInProgress == 0)
            {
                // Reset the trade profitability counter every day and get the number of trades taken in total.
                if (Bars.IsFirstBarOfSession && IsFirstTickOfBar)
                {
                    InitializeTradeAccounting();
                }

                if (CurrentBar < BarsRequiredToTrade)
                    return;

                ReversalTrade();
            }
            // When the OnBarUpdate() is called from the secondary bar series, do nothing.
            else
            {
                //AdjustTargetStopLoss();
                return;
            }
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            // Handle entry orders here. The entryOrder object allows us to identify that the order that is calling the OnOrderUpdate() method is the entry order.
            // Assign entryOrder in OnOrderUpdate() to ensure the assignment occurs when expected.
            // This is more reliable than assigning Order objects in OnBarUpdate, as the assignment is not gauranteed to be complete if it is referenced immediately after submitting
            if (order.Name == "Long" || order.Name == "Short")
            {
                entryOrder = order;

                // Reset the entryOrder object to null if order was cancelled without any fill
                if (order.OrderState == OrderState.Cancelled && order.Filled == 0)
                {
                    entryOrder = null;
                    sumFilled = 0;
                }
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            /* We advise monitoring OnExecution to trigger submission of stop/target orders instead of OnOrderUpdate() since OnExecution() is called after OnOrderUpdate()
            which ensures your strategy has received the execution which is used for internal signal tracking. */
            if (entryOrder != null && entryOrder == execution.Order)
            {
                if (execution.Order.OrderState == OrderState.Filled || execution.Order.OrderState == OrderState.PartFilled || (execution.Order.OrderState == OrderState.Cancelled && execution.Order.Filled > 0))
                {
                    // We sum the quantities of each execution making up the entry order
                    sumFilled += execution.Quantity;

                    if (execution.Order.Name == "Long")
                    {
                        // Submit exit orders for partial fills
                        if (execution.Order.OrderState == OrderState.PartFilled)
                        {
                            stopOrder = ExitLongStopMarket(0, true, execution.Order.Filled, execution.Order.AverageFillPrice - stopLoss * TickSize, "MyStop", "Long");
                            targetOrder = ExitLongLimit(0, true, execution.Order.Filled, execution.Order.AverageFillPrice + profiltsTaking * TickSize, "MyTarget", "Long");
                        }
                        // Update our exit order quantities once orderstate turns to filled and we have seen execution quantities match order quantities
                        else if (execution.Order.OrderState == OrderState.Filled && sumFilled == execution.Order.Filled)
                        {
                            // Stop-Loss order for OrderState.Filled
                            stopOrder = ExitLongStopMarket(0, true, execution.Order.Filled, execution.Order.AverageFillPrice - stopLoss * TickSize, "MyStop", "Long");
                            targetOrder = ExitLongLimit(0, true, execution.Order.Filled, execution.Order.AverageFillPrice + profiltsTaking * TickSize, "MyTarget", "Long");
                        }
                    }
                    else if (execution.Order.Name == "Short")
                    {
                        // Submit exit orders for partial fills
                        if (execution.Order.OrderState == OrderState.PartFilled)
                        {
                            stopOrder = ExitShortStopMarket(0, true, execution.Order.Filled, execution.Order.AverageFillPrice + stopLoss * TickSize, "MyStop", "Short");
                            targetOrder = ExitShortLimit(0, true, execution.Order.Filled, execution.Order.AverageFillPrice - profiltsTaking * TickSize, "MyTarget", "Short");
                        }
                        // Update our exit order quantities once orderstate turns to filled and we have seen execution quantities match order quantities
                        else if (execution.Order.OrderState == OrderState.Filled && sumFilled == execution.Order.Filled)
                        {
                            // Stop-Loss order for OrderState.Filled
                            stopOrder = ExitShortStopMarket(0, true, execution.Order.Filled, execution.Order.AverageFillPrice + stopLoss * TickSize, "MyStop", "Short");
                            targetOrder = ExitShortLimit(0, true, execution.Order.Filled, execution.Order.AverageFillPrice - profiltsTaking * TickSize, "MyTarget", "Short");
                        }
                    }

                    // Resets the entryOrder object and the sumFilled counter to null / 0 after the order has been filled
                    if (execution.Order.OrderState != OrderState.PartFilled && sumFilled == execution.Order.Filled)
                    {
                        entryOrder = null;
                        sumFilled = 0;
                    }
                }
            }

            // Reset our stop order and target orders' Order objects after our position is closed. (1st Entry)
            if ((stopOrder != null && stopOrder == execution.Order) || (targetOrder != null && targetOrder == execution.Order))
            {
                if (execution.Order.OrderState == OrderState.Filled || execution.Order.OrderState == OrderState.PartFilled)
                {
                    stopOrder = null;
                    targetOrder = null;
                }
            }
        }
    }
}
