#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
using System.Diagnostics;
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
	public class AGG5 : Strategy
	{
        private int Fast;
        private int Slow;

        private Order entryOrder = null; // This variable holds an object representing our entry order
        private Order stopOrder = null; // This variable holds an object representing our stop loss order
        private Order targetOrder = null; // This variable holds an object representing our profit target order
        private int sumFilled = 0; // This variable tracks the quantities of each execution making up the entry order

        private string svrSignal = "1";

        private static readonly int lotSize = 1;

        private static readonly int profitChasing = 20*4; // 4 ticks below profitTarget
        private static readonly int profitTarget = profitChasing*10; // for automatic profits taking, profit chasing will take care of profit taking once profit > profitChasing
        private static readonly int softDeck = 10*4; // number of ticks for soft stop loss
        private static readonly int hardDeck = 12*4; //hard deck for auto stop loss
        private double closedPrice = 0.0;
        private double chasePrice = 0.0;

        // global flags
        private bool profitChasingFlag = false;

        private Socket sender = null; 
        private byte[] bytes = new byte[1024];
        int lineNo = 0;

        enum Position
        { 
            posFlat,
            posShort,
            posLong
        };
        Position currPos = Position.posFlat;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"AGG5 strategy, using DLNN to manage start new position and stop loss, profit chasing depends on market trend";
                Name = "AGG5Tick";
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
                BarsRequiredToTrade = 5;
                Fast = 10;
                Slow = 25;

                //Set this scripts Print() calls to the first output tab
                PrintTo = PrintTo.OutputTab1;

                // Connect to DLNN Server  
                try
                {
                    // Do not attempt connection if already connected
                    if (sender!=null)
                        return;

                    // Establish the remote endpoint for the socket.  
                    // connecting server on port 3333  
                    IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
                    IPAddress ipAddress = ipHostInfo.AddressList[1];
                    IPEndPoint remoteEP = new IPEndPoint(ipAddress, 3333);

                    // Create a TCP/IP  socket.  
                    sender = new Socket(ipAddress.AddressFamily,
                        SocketType.Stream, ProtocolType.Tcp);

                    // Connect the socket to the remote endpoint. Catch any errors.  
                    try
                    {
                        sender.Connect(remoteEP);
 
                        Print(" ************ Socket connected to : " + 
                            sender.RemoteEndPoint.ToString() + "*************");

                        // TODO: Release the socket.  
                        //sender.Shutdown(SocketShutdown.Both);
                        //sender.Close();

                    }
                    catch (ArgumentNullException ane)
                    {
                        Print("ArgumentNullException : " + ane.ToString());
                    }
                    catch (SocketException se)
                    {
                        Print("SocketException : " + se.ToString());
                    }
                    catch (Exception e)
                    {
                        Print("Unexpected exception : " + e.ToString());
                    }

                }
                catch (Exception e)
                {
                    Print(e.ToString());
                }
            }
            else if (State == State.Configure)
            {
                /* Add a secondary bar series. 
				Very Important: This secondary bar series needs to be smaller than the primary bar series.
				
				Note: The primary bar series is whatever you choose for the strategy at startup. 
				In our case it is a 2000 ticks bar. */
                AddDataSeries(Data.BarsPeriodType.Tick, 1);

    //            // Add two EMA indicators to be plotted on the primary bar series
    //            AddChartIndicator(EMA(Fast));
    //            AddChartIndicator(EMA(Slow));

    //            /* Adjust the color of the EMA plots.
				//For more information on this please see this tip: http://www.ninjatrader-support.com/vb/showthread.php?t=3228 */
    //            EMA(Fast).Plots[0].Brush = Brushes.Blue;
    //            EMA(Slow).Plots[0].Brush = Brushes.Green;


                // set static profit target and stop loss
                SetProfitTarget(CalculationMode.Ticks, profitTarget);
                SetStopLoss(CalculationMode.Ticks, hardDeck);
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

        protected override void OnAccountItemUpdate(Cbi.Account account, Cbi.AccountItem accountItem, double value)
		{
			
		}

		protected override void OnConnectionStatusUpdate(ConnectionStatusEventArgs connectionStatusUpdate)
		{
			
		}

        //protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        //{
        //    /* We advise monitoring OnExecution to trigger submission of stop/target orders instead of OnOrderUpdate() since OnExecution() is called after OnOrderUpdate()
        //    which ensures your strategy has received the execution which is used for internal signal tracking. */
        //    if (entryOrder != null && entryOrder == execution.Order)
        //    {
        //        if (execution.Order.OrderState == OrderState.Filled || execution.Order.OrderState == OrderState.PartFilled || (execution.Order.OrderState == OrderState.Cancelled && execution.Order.Filled > 0))
        //        {
        //            // We sum the quantities of each execution making up the entry order
        //            sumFilled += execution.Quantity;

        //            if (execution.Order.Name == "Long")
        //            {
        //                // Submit exit orders for partial fills
        //                if (execution.Order.OrderState == OrderState.PartFilled)
        //                {
        //                    stopOrder = ExitLongStopMarket(0, true, execution.Order.Filled, execution.Order.AverageFillPrice - hardDeck * TickSize, "MyStop", "Long");
        //                    targetOrder = ExitLongLimit(0, true, execution.Order.Filled, execution.Order.AverageFillPrice + profitTarget * TickSize, "MyTarget", "Long");
        //                }
        //                // Update our exit order quantities once orderstate turns to filled and we have seen execution quantities match order quantities
        //                else if (execution.Order.OrderState == OrderState.Filled && sumFilled == execution.Order.Filled)
        //                {
        //                    // Stop-Loss order for OrderState.Filled
        //                    stopOrder = ExitLongStopMarket(0, true, execution.Order.Filled, execution.Order.AverageFillPrice - hardDeck * TickSize, "MyStop", "Long");
        //                    targetOrder = ExitLongLimit(0, true, execution.Order.Filled, execution.Order.AverageFillPrice + profitTarget * TickSize, "MyTarget", "Long");
        //                }
        //            }
        //            else if (execution.Order.Name == "Short")
        //            {
        //                // Submit exit orders for partial fills
        //                if (execution.Order.OrderState == OrderState.PartFilled)
        //                {
        //                    stopOrder = ExitShortStopMarket(0, true, execution.Order.Filled, execution.Order.AverageFillPrice + hardDeck * TickSize, "MyStop", "Short");
        //                    targetOrder = ExitShortLimit(0, true, execution.Order.Filled, execution.Order.AverageFillPrice - profitTarget * TickSize, "MyTarget", "Short");
        //                }
        //                // Update our exit order quantities once orderstate turns to filled and we have seen execution quantities match order quantities
        //                else if (execution.Order.OrderState == OrderState.Filled && sumFilled == execution.Order.Filled)
        //                {
        //                    // Stop-Loss order for OrderState.Filled
        //                    stopOrder = ExitShortStopMarket(0, true, execution.Order.Filled, execution.Order.AverageFillPrice + hardDeck * TickSize, "MyStop", "Short");
        //                    targetOrder = ExitShortLimit(0, true, execution.Order.Filled, execution.Order.AverageFillPrice - profitTarget * TickSize, "MyTarget", "Short");
        //                }
        //            }

        //            // Resets the entryOrder object and the sumFilled counter to null / 0 after the order has been filled
        //            if (execution.Order.OrderState != OrderState.PartFilled && sumFilled == execution.Order.Filled)
        //            {
        //                entryOrder = null;
        //                sumFilled = 0;
        //            }
        //        }
        //    }

        //    // Reset our stop order and target orders' Order objects after our position is closed. (1st Entry)
        //    if ((stopOrder != null && stopOrder == execution.Order) || (targetOrder != null && targetOrder == execution.Order))
        //    {
        //        if (execution.Order.OrderState == OrderState.Filled || execution.Order.OrderState == OrderState.PartFilled)
        //        {
        //            stopOrder = null;
        //            targetOrder = null;
        //        }
        //    }
        //}

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            // Handle entry orders here. The entryOrder object allows us to identify that the order that is calling the OnOrderUpdate() method is the entry order.
            // Assign entryOrder in OnOrderUpdate() to ensure the assignment occurs when expected.
            // This is more reliable than assigning Order objects in OnBarUpdate, as the assignment is not gauranteed to be complete if it is referenced immediately after submitting
            if (order.Name == "Long" || order.Name == "Short")
            {
                entryOrder = order;

                if (order.Filled == 1)
                {
                    closedPrice = order.AverageFillPrice;
                    if (order.Name == "Long")
                        currPos = Position.posLong;
                    if (order.Name == "Short")
                        currPos = Position.posShort;

                    Print(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " *********Order filed, order name=" + order.Name + " currPos=" + currPos.ToString());
                }

                // Reset the entryOrder object to null if order was cancelled without any fill
                if (order.OrderState == OrderState.Cancelled && order.Filled == 0)
                {
                    entryOrder = null;
                    sumFilled = 0;
                }
            }
        }

        // WARNING!!!! Market position is not order position
        protected override void OnPositionUpdate(Cbi.Position position, double averagePrice, 
			int quantity, Cbi.MarketPosition marketPosition)
		{
            if (position.MarketPosition == MarketPosition.Flat)
            {
            }
            if (position.MarketPosition == MarketPosition.Long)
            {
            }
            if (position.MarketPosition == MarketPosition.Short)
            {
            }
        }

        private string ExtractResponse(string repStr)
        {
            int index = 1;
            foreach (char ch in repStr)
            {
                if (ch != ',')
                    index++;
                else
                    break;
            }

            return repStr.Substring(index, 1);
        }

        private bool PosFlat()
        {
            return (currPos == Position.posFlat);
        }

        private bool PosShort()
        {
            return (currPos == Position.posShort);
        }
        
        private bool PosLong()
        {
            return (currPos == Position.posLong);
        }

        private void AiShort()
        {
            EnterShort(lotSize, "Short");
            Print(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " Server Signal=" + svrSignal + " Short");
        }

        private void AiLong()
        {
            EnterLong(lotSize, "Long");
            Print(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " Server Signal=" + svrSignal + " Long");
        }
        
        private void AiFlat()
        {
            Print("AiFlat: currPos = " + currPos.ToString());
            if (PosLong())
            {
                ExitLong("ExitLong", "Long");
                Print(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " AiFlat::ExitLong");
                Print("---------------------------------------------------------------------------------");
            }
            if (PosShort())
            {
                ExitShort("ExitShort", "Short");
                Print(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " AiFlat::ExitShort");
                Print("---------------------------------------------------------------------------------");
            }

            //reset global flags
            currPos = Position.posFlat;
            profitChasingFlag = false;
        }

        private void StartTradePosition(string signal)
        {
            //Print("StartTradePosition");
            switch (signal)
            {
                case "0":
                    // sell
                    AiShort();
                    break;
                case "2":
                    // buy
                    AiLong();
                    break;
                default:
                    // do nothing if signal is 1 for flat position
                    break;
            }
        }

        private void ExecuteAITrade(string signal)
        {
            //Print("ExecuteAITrade");
            if (PosFlat())
            {
                StartTradePosition(signal);
                return;
            }

            // if there is an existing Long or Short position, handle them in the tick by tick section of the OnBarUpdate()
            if (PosLong() || PosShort())
            {
                return;
            }
        }

        private void HandleSoftDeck(string signal)
        {
            if (PosFlat())
            {
                // this is not possible
                Debug.Assert(!PosFlat(), "ASSERT: Position is flat while HandleSoftDeck");
                return;
            }

            // if there is an existing position, handle them in the tick by tick section of the OnBarUpdate()
            if (PosLong())
            {
                if (signal != "2")
                {
                    Print(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " HandleSoftDeck:: signal= " + signal.ToString() + " current price=" + Close[0] + " closedPrice=" + closedPrice.ToString() + " soft deck=" + (softDeck* TickSize).ToString());
                    AiFlat();
                }
                return;
            }

            if (PosShort())
            {
                if (signal != "0")
                {
                    Print(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " HandleSoftDeck:: signal= " + signal.ToString() + " current price=" + Close[0] + " closedPrice=" + closedPrice.ToString() + " soft deck=" + (softDeck* TickSize).ToString());
                    AiFlat();
                }
                return;
            }
        }

        private bool ViolateSoftDeck()
        {
            if (PosLong())
            {
                return (Close[0] <= (closedPrice - softDeck* TickSize));
            }
            if (PosShort())
            {
                return (Close[0] >= (closedPrice + softDeck* TickSize));
            }
            return false;
        }

        private void HandleProfitChasing()
        {
            if (PosFlat())
            {
                // this is not possible
                Debug.Assert(!PosFlat(), "ASSERT: Position is flat while HandleProfitChasing");
                return;
            }
            // if market trend go against profit positions, then flatten position and take profits
            if (PosLong())
            {
                if (Close[0] < chasePrice)
                {
                    Print(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " HandleProfitChasing::" + " currPos=" + currPos.ToString() + " closedPrice=" + closedPrice.ToString() + " Close[0]=" + Close[0].ToString() + " chasePrice=" + chasePrice.ToString() + " closedPrice + profitChasing=" + (closedPrice + profitChasing * TickSize).ToString() + " Profits=" + (Close[0] - closedPrice).ToString());
                    AiFlat();
                }
                else
                    chasePrice = Close[0];
            }
            if (PosShort())
            {
                if (Close[0] > chasePrice)
                {
                    Print(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " HandleProfitChasing::" + " currPos=" + currPos.ToString() + " closedPrice=" + closedPrice.ToString() + " Close[0]=" + Close[0].ToString() + " chasePrice=" + chasePrice.ToString() + " closedPrice - profitChasing=" + (closedPrice - profitChasing * TickSize).ToString() + " Profits=" + (closedPrice - Close[0]).ToString());
                    AiFlat();
                }
                else
                    chasePrice = Close[0];
            }
        }

        private bool TouchedProfitChasing()
        {
            profitChasingFlag = false;

            if (PosLong())
            {
                if  (Close[0] >= (closedPrice + profitChasing*TickSize))
                {
                    Print(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " TouchedProfitChasing");
                    profitChasingFlag = true;
                    chasePrice = Close[0];
                    return profitChasingFlag;
                }
            }
            if (PosShort())
            {
                if (Close[0] <= (closedPrice - profitChasing*TickSize))
                {
                    Print(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " TouchedProfitChasing");
                    profitChasingFlag = true;
                    chasePrice = Close[0];
                    return profitChasingFlag;
                }
            }

            return profitChasingFlag;
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
                if (CurrentBar < BarsRequiredToTrade)
                    return;

                // if the bar elapsed time span across 12 mid night
                DateTime t1 = Bars.GetTime(CurrentBar - 1);
                DateTime t2 = Bars.GetTime(CurrentBar);
                if (TimeSpan.Compare(t1.TimeOfDay, t2.TimeOfDay) > 0)
                {
                    Print("EOD Session");
                    AiFlat(); // flatten the current position before going over to the next day
                    lineNo = 0;
                    return;
                }

                // construct the string buffer to be sent to DLNN
                string bufString = lineNo.ToString() + ',' + 
                    Bars.GetTime(CurrentBar-1).ToString("HHmmss") + ',' + Bars.GetTime(CurrentBar).ToString("HHmmss") + ',' +
                    Bars.GetOpen(CurrentBar).ToString() + ',' + Bars.GetClose(CurrentBar).ToString() + ',' +
                    Bars.GetHigh(CurrentBar).ToString() + ',' + Bars.GetLow(CurrentBar).ToString() + ',' +
                    Bars.GetVolume(CurrentBar).ToString() + ',' +
                    SMA(9)[0].ToString() + ',' + SMA(20)[0].ToString() + ',' + SMA(50)[0].ToString() + ',' +
                    MACD(12, 26, 9).Diff[0].ToString() + ',' + RSI(14, 3)[0].ToString() + ',' +
                    Bollinger(2, 20).Lower[0].ToString() + ',' + Bollinger(2, 20).Upper[0].ToString() + ',' +
                    CCI(20)[0].ToString() + ',' +
                    Bars.GetHigh(CurrentBar).ToString() + ',' + Bars.GetLow(CurrentBar).ToString() + ',' +
                    Momentum(20)[0].ToString() + ',' +
                    DM(14).DiPlus[0].ToString() + ',' + DM(14).DiMinus[0].ToString() + ',' +
                    VROC(25, 3)[0].ToString() + ',' +
                    '0' + ',' + '0' + ',' + '0' + ',' + '0' + ',' + '0' + ',' +
                    '0' + ',' + '0' + ',' + '0' + ',' + '0' + ',' + '0';

                //Print("CurrentBar = " + CurrentBar + ": " + "bufString = " + bufString);

                byte[] msg = Encoding.UTF8.GetBytes(bufString);

                // Send the data through the socket.  
                int bytesSent = sender.Send(msg);

                // Receive the response from the remote device.  
                int bytesRec = sender.Receive(bytes);

                svrSignal = ExtractResponse(System.Text.Encoding.UTF8.GetString(bytes, 0, bytes.Length));


                //Print("Server response= " + svrSignal);

                // Return signal from DLNN is not we expected, close outstanding position and restart
                if (bytesRec == -1)
                {
                    lineNo = 0;
                    // TODO: close current position
                }
                else
                    lineNo++;

                // Start processing signal after 8th signal and beyond, otherwise ignore
                if (lineNo >= 8)
                    ExecuteAITrade(svrSignal);
            }
            // When the OnBarUpdate() is called from the secondary bar series, in our case for each tick, handle stop loss and profit chasing accordingly
            else
            {
                // if position is flat, no need to do anything
                if (currPos == Position.posFlat)
                    return;

                // handle stop loss or proft chasing if there is existing position and order action is either SellShort or Buy
                if (entryOrder != null && (entryOrder.OrderAction == OrderAction.Buy || entryOrder.OrderAction == OrderAction.SellShort) && (entryOrder.OrderState == OrderState.Filled || entryOrder.OrderState == OrderState.PartFilled))
                {
                    // if Close[0] violates soft deck, if YES handle stop loss accordingly
                    if (ViolateSoftDeck())
                    {
                        HandleSoftDeck(svrSignal);
                    }

                    // if profitChasingFlag is TRUE or Close[0] TouchedProfitChasing then handle profit chasing
                    if ((profitChasingFlag || TouchedProfitChasing()))
                    {
                        HandleProfitChasing();
                    }
                }
                return;
            }
        }
    }
}
