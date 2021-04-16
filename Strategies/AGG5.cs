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

        private int profiltsTaking = 18; // number of ticks for profits taking
        private int stopLoss = 18; // number of ticks for stop loss

        private Socket sender = null; 
        private byte[] bytes = new byte[512];
        int lineNo = 0;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Sample Using OnOrderUpdate() and OnExecution() methods to submit protective orders";
                Name = "AGG5";
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

        protected override void OnAccountItemUpdate(Cbi.Account account, Cbi.AccountItem accountItem, double value)
		{
			
		}

		protected override void OnConnectionStatusUpdate(ConnectionStatusEventArgs connectionStatusUpdate)
		{
			
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

        protected override void OnPositionUpdate(Cbi.Position position, double averagePrice, 
			int quantity, Cbi.MarketPosition marketPosition)
		{
			
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

            return repStr.Substring(index, index);
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

                //Print("Bars Visible: " + (ChartBars.ToIndex - ChartBars.FromIndex));
                Print("CurrentBar = " + CurrentBar + ": " + "bufString = " + bufString);

                // if the bar elapsed time span across 12 mid night
                DateTime t1 = Bars.GetTime(CurrentBar - 1);
                DateTime t2 = Bars.GetTime(CurrentBar);
                if (TimeSpan.Compare(t1.TimeOfDay, t2.TimeOfDay) > 0)
                {
                    lineNo = 0;
                    Print("EOD Session");
                    return;
                }

                byte[] msg = Encoding.UTF8.GetBytes(bufString);

                // Send the data through the socket.  
                int bytesSent = sender.Send(msg);

                // Receive the response from the remote device.  
                int bytesRec = sender.Receive(bytes);

                Print("Server response UTF8: " +
                    System.Text.Encoding.UTF8.GetString(bytes, 0, bytes.Length));

                string SRepStr = System.Text.Encoding.UTF8.GetString(bytes, 0, bytes.Length);
                Print("Server response: " + ExtractResponse(SRepStr));

                if (bytesRec == -1)
                    lineNo = 0;
                else
                    lineNo++;

            }
            // When the OnBarUpdate() is called from the secondary bar series, do nothing.
            else
            {
                return;
            }
        }
    }
}
