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
using System.IO;
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Strategies
{
    public class SP500EminiLiveAITrader3002 : Strategy
    {
        // log, error, current capital and vix  files
        private string pathLog;
        private string pathErr;
        private string pathCC;
        private string pathCL;
        private string pathVIX;
        private string pathPstops;
        private string pathMktView;
        private StreamWriter swLog = null; // runtime log file 
        private StreamWriter swErr = null; // error file
        private StreamWriter swCC = null;  // Store current capital for each strategy 
        private StreamWriter swCL = null;  // Store current monthly losses for each strategy
        private StreamWriter swVIX = null;  // Store 10 days Moving average VIX
        private StreamWriter swPstops = null; // Store dynamic Pstops
        private StreamWriter swMkt = null; // Store marekt view, 0=Bear, 1=Neutral, 2=Bull

        private Order entryOrder = null; // This variable holds an object representing our entry order
        private Order stopOrder = null; // This variable holds an object representing our stop loss order
        private Order targetOrder = null; // This variable holds an object representing our profit target order


        /* **********************************************************************************************************
         * Following settings need to be set before run
         * **********************************************************************************************************
         */
        // these constants affects how the drawdown policy is being enforced,  
        // current optimal low vix settings 7-5-2 / 60-30-10, high vix >= 40 settings 4-2-2 / 75-10-5
        private static double HighVixTreshold = 40;

        //below are Daily drawdown (counting wins and losses) strategy settings
        // Low VIX daily drawdown control settings
        //private static int LVmaxConsecutiveLossesUpper = 7;  // upper limit allowable daily losses
        //private static int LVmaxConsecutiveLosses = 5;      // max allowable daily losses if no win
        //private static int LVminConsecutiveWins = 2;       // min wins to increment max allowable daily losses 
        private static int LVmaxConsecutiveLossesUpper = 4;  // upper limit allowable daily losses
        private static int LVmaxConsecutiveLosses = 2;      // max allowable daily losses if no win
        private static int LVminConsecutiveWins = 2;       // min wins to increment max allowable daily losses 
        // High VIX daily drawdown control settings
        private static int HVmaxConsecutiveLossesUpper = 4; // upper limit allowable daily losses
        private static int HVmaxConsecutiveLosses = 2;     // max allowable daily losses if no win
        private static int HVminConsecutiveWins = 2;      // min wins to increment max allowable daily losses

        //below are Monthly drawdown (Profit chasing and stop loss) strategy settings
        //Low VIX monthly drawdown control settings
        private static double LVprofitChasingTarget = 0.3; // % monthly gain profit target
        private static double LVmaxPercentAllowableDrawdown = 0.15; // allowable maximum % monthly drawdown if profit target did not achieve before trading halt for the month
        private static double LVprofitChasingAllowableDrawdown = 0.1; // allowable max % drawdown if profit chasing target is achieved before trading halt for the month
        // High VIX monthly drawdown control settings
        private static double HVprofitChasingTarget = 0.75; // % monthly gain profit target
        private static double HVmaxPercentAllowableDrawdown = 0.1; // allowable maximum % monthly drawdown if profit target did not achieve before trading halt for the month
        private static double HVprofitChasingAllowableDrawdown = 0.05; // allowable max % drawdown if profit chasing target is achieved before trading halt for the month

        // --------------------------------------------------
        // TRADE FILTERS
        // --------------------------------------------------
        // flags and settings for VROC, CCI, DMR, RSI and ADX trade filters, EITHER CCI, RSI OR ADX can be TRUE at ANY ONE TIME
        private static bool DMREnabled = false;
        private static bool UseADXFilter = false;
        private static bool UseCCIFilter = false;
        private static bool UseVROCFilter = true;
        private static bool UseRSIFilter = false;
        private static bool UseMACDFilter = false;
        private static bool UserMACDAndVROC = false;
        private static bool UseRSIAndMACDFilter = false;
        private static bool UseRSIAndVROCFilter = false;
        private static bool UseADXAndVROCFilter = false;
        private static bool UseRSIADXandVROCFilter = false;
        private static bool UseMACDorRSIandVROCFilter = false; // (MACD || RSI) && VROC 
        // When set to TRUE, trade when MACD_Diff < MACDDiffThreshold, 
        // When set to FALSE, trade when MACD_Diff > MACDDiffThreshold
        private static bool UseMACDInLessThanMode = false;

        // Handle early position exit with HandleMarketShift
        private static bool UseEntryFilter = true;
        private static bool UseExitFilter = true;

        // Macro Market Views
        enum MarketView
        {
            Bullish,
            Bearish,
            Neutral
        };
        MarketView currMarketView;

        // --------------------------------------------------
        // TRADE FILTERS THRESHOLDS
        // --------------------------------------------------
        private static double RSIUpper = 75;
        private static double RSILower = 30;
        private static double CCIUpper = 180;
        private static double CCILower = -180;
        private static double ADXThreshold = 45;
        private static double VROCUpper = 13;
        private static double VROCLower = -14;
        private static double VROCPos = 2.0;
        private static double VROCNeg = -2.0;
        private static double MACDDiffThreshold = 0.2;
        private static double ProfitPercentage = 0.75;  // 75% Profit target met to use SMA Exit filter
        private static int SMAConstant = 20; // Usually 20 or 9
        private bool profitPercentMet = false;

        // initial trading capital and trading lot size
        private static readonly int LotSize = 100;

        // Dollar value for ONE point, i.e. 4 ticks, 4 x $12.50 (value per tick) = $50
        private static double dollarValPerPoint = 50;

        // IMPORTANT: initial starting capital is set to $10,000 for monthly drawdown control strategy accounting purpose, 
        //            the monthly drawdown comparison is based on %percentage% of $10,000
        //            even though capital to lot ratio can be set to $25,000 per lot
        private static double InitStartingCapital = 10000 * LotSize;

        /* **********************************************************************************************************
         * Commission rate needs to be set to the current commission rate
         * **********************************************************************************************************
         */
        private static double CommissionRate = 5.58 * LotSize;
        /*
         * **********************************************************************************************************
         */

        // these variables affects how the daily drawdown policy is being enforced
        private int maxConsecutiveLossesUpper = LVmaxConsecutiveLossesUpper;
        private int maxConsecutiveLosses = LVmaxConsecutiveLossesUpper;
        private int minConsecutiveWins = LVmaxConsecutiveLossesUpper;
        private int initMaxConsecutiveLosses;

        // these variables affects how the monthly drawdown policy is being enforced 
        private double profitChasingTarget = LVprofitChasingTarget; // % monthly gain profit target
        private double maxPercentAllowableDrawdown = LVmaxPercentAllowableDrawdown; // allowable maximum % monthly drawdown if profit target did not achieve before trading halt for the month
        private double profitChasingAllowableDrawdown = LVprofitChasingAllowableDrawdown; // allowable max % drawdown if profit chasing target is achieved before trading halt for the month

        private double virtualCurrentCapital = InitStartingCapital; // set to startingCapital before the day
        private double currentMonthlyLosses = 0; // starts with zero losses for the monthly 

        // below are variables accounting for each trading day, tracking monthly drawdown control strategy
        // they are to be initialized when State == State.DataLoaded during start up
        private double yesterdayVirtualCapital = InitStartingCapital; // set to  InitStartingCapital before the run, it will get initialized when State == State.Realtime
        private bool monthlyProfitChasingFlag = false; // set to false before the month
        private double lastTotalRealtimePnL = 0;

        private int maxConsecutiveDailyLosses = LVmaxConsecutiveLosses;
        private int consecutiveDailyLosses = 0;
        private int consecutiveDailyWins = 0;

        private string svrSignal = "1";

        /* **********************************************************************************************************
         * Following settings need to be set once
         * **********************************************************************************************************
         */
        private static readonly int TicksPerStop = 4;
        private static readonly int defaultPstops = 20;
        private static readonly int defaultLstops = 10;
        private int profitChasing = defaultPstops * TicksPerStop; // the target where HandleProfitChasing kicks in
        private int softDeck = defaultLstops * TicksPerStop; // number of stops for soft stop loss
        private int hardDeck = defaultPstops * TicksPerStop; //hard deck for auto stop loss
        private int pStops, lStops;
        private static readonly int portNumber = 3002;
        private static readonly string hostName = "AITrader";
        /*
         * **********************************************************************************************************
         */
        private double closedPrice = 0.0;
        // *** NOTE ***: NEED TO MODIFY the HH and MM of the endSessionTime to user needs, always minus bufferUntilEOD minutes to allow for buffer checking of end of session time, e.g. 23HH 59-10MM
        private static int bufferUntilEOD = 10;  // number of minutes before end of session
        private DateTime regularEndSessionTime = new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day, 15, (15 - bufferUntilEOD), 00);
        private DateTime fridayEndSessionTime = new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day, 15, (15 - bufferUntilEOD), 00);
        private bool endSession = false;
        private bool firstBarOfDay = true;

        // global flags
        private bool profitChasingFlag = false;
        //private bool stopLossEncountered = false;
        private bool attemptToFlattenPos = false;
        private bool haltTrading = false;

        private Socket sender = null;
        private byte[] bytes = new byte[1024];
        int lineNo = 0;

        private double highOfDay = 0;
        private double lowOfDay = 9999999999;

        enum Position
        {
            posFlat,
            posShort,
            posLong
        };
        Position currPos = Position.posFlat;

        enum ErrorType
        {
            verbose,
            normal,
            warning,
            fatal
        };

        private static ErrorType defaultErrorType = ErrorType.verbose;

        enum ExitOrderType
        {
            limit,
            market
        };

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                MyPrint(defaultErrorType, "State == State.SetDefaults");

                Description = @"Implements live trading for the daily drawdown control and monthly profit chasing/stop loss strategy, using limit order.";
                Name = "SP500EminiLiveAITrader3002";
                //Calculate = Calculate.OnEachTick; // don't need this, taken care of with AddDataSeries(Data.BarsPeriodType.Tick, 1);
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;           //only 1 position in each direction (long/short) at a time per strategy
                EntryHandling = EntryHandling.AllEntries; //the above restriction applies to all entries regardless of their naming, e.g. EnterLong(LotSize, "Long"), Long is the naming;
                IsExitOnSessionCloseStrategy = true;  //all positions (if still open) will be closed at session close
                ExitOnSessionCloseSeconds = 30; //The number of seconds before the actual session end time that the "IsExitOnSessionCloseStrategy" function will trigger.
                IsFillLimitOnTouch = false; //Determines if the strategy will use a more liberal fill algorithm for back-testing purposes only
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix; //When using MaximumBarsLookBack.TwoHundredFiftySix, only the last 256 values of the series object will be stored in memory and be accessible for reference
                OrderFillResolution = OrderFillResolution.Standard; //Determines how strategy orders are filled during historical states. Backtesting purpose
                Slippage = 0; //Sets the amount of slippage in ticks per execution used in performance calculations during backtests.
                /*
                 * When network goes down and subsequently reconnected, the following behaviors are expected:
                 * - If the Account Position is flat already, no reconciliatory order will be submitted. 
                 *      The strategy will then wait for the Strategy Position to reach a flat state as well before submitting any orders live.
                 * - If the Account Position is not flat, NinjaTrader will submit a market order(s) to reconcile the Account Position to a flat state. 
                 *      The strategy will then wait for the Strategy Position to reach a flat state before submitting live orders.
                 *   
                 *   The outcome is that NT strategy code will ensure all virtual positions to be flatten, and NT platform will ensure all account positions to be flatten.
                 */
                StartBehavior = StartBehavior.WaitUntilFlatSynchronizeAccount;

                TimeInForce = TimeInForce.Gtc; //Sets the time in force property for all orders generated by a strategy. Order will remain working until the order is explicitly cancelled.

                //Determines if OnOrderTrace() would be called for a given strategy.  When enabled, traces are generated and displayed in the NinjaScript Output window for each call of an order method providing confirmation that the method is entered and providing information if order methods are ignored and why. 
                //This is valuable for debugging if you are not seeing expected behavior when calling an order method. 
                TraceOrders = true;

                //Defines the behavior of a strategy when a strategy generated order is returned from the broker's server in a "Rejected" state.
                //RealtimeErrorHandling.StopCancelClose is the default behavior, it will stop the strategy and cancel the order
                //RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                //IBKR reports error even when the order goes through, resulting in NT issuing a new order position if StopCancelClose is used
                // hence have to IgnoreAllErrors and rely on manual closing of outstanding positions
                RealtimeErrorHandling = RealtimeErrorHandling.IgnoreAllErrors;

                //Determines how stop and target orders are submitted during an entry order execution. 
                //StopTargetHandling.ByStrategyPosition means Stop and Target order quantities will match the current strategy position.  (Stops and targets may result in "stacked" orders on partial fills)
                //If you would prefer all of your stops and targets to be placed at the same time within the same order, it is suggested to use StopTargetHandling.ByStrategyPosition. 
                //However this may result in more stop and target orders being submitted than the overall strategy position in a scenario in which the strategy's entire entry orders are not filled in one fill.
                StopTargetHandling = StopTargetHandling.ByStrategyPosition;

                BarsRequiredToTrade = 0; //The number of historical bars required before the strategy starts processing order methods called in the OnBarUpdate() method.

                //Sets the manner in which your strategy will behave when a connection loss is detected.
                //When using ConnectionLossHandling.Recalculate, recalculations will only occur if the strategy was stopped based on the conditions below.
                //�If data feed disconnects for longer than the time specified in DisconnectDelaySeconds, currently set at 10 secs, the strategy is stopped.
                //�If the order feed disconnects and the strategy places an order action while disconnected, the strategy is stopped.
                //�If both the data and order feeds disconnect for longer than the time specified in DisconnectDelaySeconds, currently set at 10 secs, the strategy is stopped.
                //Strategies will attempt to recalculate its strategy position when a connection is reestablished.
                ConnectionLossHandling = ConnectionLossHandling.Recalculate;
            }
            else if (State == State.Configure)
            {
                MyPrint(defaultErrorType, "State == State.Configure");

                /* Add a secondary bar series.
                   Very Important: This secondary bar series needs to be smaller than the primary bar series.

                   Note: The primary bar series is whatever you choose for the strategy at startup.
                   In our case it is a 2000 ticks bar. */
                AddDataSeries(Data.BarsPeriodType.Tick, 100);

                // Add daily VIX data series
                AddDataSeries("^VIX", BarsPeriodType.Day, 1);

                //SetProfitTarget and SetStopLoss can not be used together with ExitLongLimit and ExitShortLimit, let HandleSoftDeck and HandleHardDeck handles the Exit.
                //set static profit target and stop loss, this will ensure outstanding Account Positions are protected automatically
                //MyPrint("Set static profit target and stop loss (ticks), profitTarget=" + profitTarget + " hardDeck=" + hardDeck);
                //SetProfitTarget(CalculationMode.Ticks, profitTarget);
                //SetStopLoss(CalculationMode.Ticks, hardDeck);
            }
            else if (State == State.Realtime)
            {
                MyPrint(ErrorType.warning, "State == State.Realtime");

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
            else if (State == State.DataLoaded)
            {
                MyPrint(ErrorType.warning, "State == State.DataLoaded");

                // Connect to DLNN Server  
                try
                {
                    // Do not attempt connection if already connected
                    if (sender != null)
                        return;

                    // Establish the remote endpoint for the socket.  
                    // connecting server on portNumber  
                    IPHostEntry ipHostInfo = Dns.GetHostEntry(hostName);

                    IPAddress ipAddress = ipHostInfo.AddressList[1]; // depending on the Wifi set up, this index may change accordingly
                    //IPAddress ipAddress = ipHostInfo.AddressList[3];
                    //ipAddress = ipAddress.MapToIPv4();
                    IPEndPoint remoteEP = new IPEndPoint(ipAddress, portNumber);

                    MyPrint(defaultErrorType, "ipHostInfo=" + ipHostInfo.HostName.ToString() + " ipAddress=" + ipAddress.ToString());

                    // Create a TCP/IP  socket.  
                    sender = new Socket(ipAddress.AddressFamily,
                        SocketType.Stream, ProtocolType.Tcp);

                    // Connect the socket to the remote endpoint. Catch any errors.  
                    try
                    {
                        sender.Connect(remoteEP);

                        MyPrint(defaultErrorType, " ************ Socket connected to : " +
                            sender.RemoteEndPoint.ToString() + "*************");

                        // set receive timeout 10 secs
                        sender.ReceiveTimeout = 10000;
                        // set send timeout 10 secs
                        sender.SendTimeout = 10000;
                    }
                    catch (ArgumentNullException ane)
                    {
                        MyErrPrint(ErrorType.fatal, "Socket Connect Error: ArgumentNullException : " + ane.ToString());
                    }
                    catch (SocketException se)
                    {
                        MyErrPrint(ErrorType.fatal, "Socket Connect Error: SocketException : " + se.ToString());
                    }
                    catch (Exception e)
                    {
                        MyErrPrint(ErrorType.fatal, "Socket Connect Error: Unexpected exception : " + e.ToString());
                    }
                }
                catch (Exception e)
                {
                    MyErrPrint(ErrorType.fatal, e.ToString());
                }

                // Setup the drawdown protections, Pstops and Lstops
                DailyTradingPolicySetup();
            }
            // Necessary to call in order to clean up resources used by the StreamWriter object
            else if (State == State.Terminated)
            {
                MyErrPrint(ErrorType.warning, "State == State.Terminated, Check for potential strategy termination due to error only captured in NT log.");

                LogFilesCleanUp();
            }
        }

        protected override void OnAccountItemUpdate(Cbi.Account account, Cbi.AccountItem accountItem, double value)
        {

        }

        protected override void OnConnectionStatusUpdate(ConnectionStatusEventArgs connectionStatusUpdate)
        {
            if (connectionStatusUpdate.Status == ConnectionStatus.Connected)
            {
                MyPrint(defaultErrorType, "OnConnectionStatusUpdate, Connected to brokerage at " + DateTime.Now);
            }

            else if (connectionStatusUpdate.Status == ConnectionStatus.ConnectionLost)
            {
                MyErrPrint(ErrorType.fatal, "OnConnectionStatusUpdate, Connection to brokerage lost at: " + DateTime.Now);
            }

            if (connectionStatusUpdate.PriceStatus == ConnectionStatus.Connected)
            {
                MyPrint(defaultErrorType, "OnConnectionStatusUpdate, Connected to data feed at " + DateTime.Now);
            }

            else if (connectionStatusUpdate.PriceStatus == ConnectionStatus.ConnectionLost)
            {
                MyErrPrint(ErrorType.fatal, "OnConnectionStatusUpdate, Connection to data feed lost at: " + DateTime.Now);
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            /* We advise monitoring OnExecution to trigger submission of stop/target orders instead of OnOrderUpdate() since OnExecution() is called after OnOrderUpdate()
            which ensures your strategy has received the execution which is used for internal signal tracking. */
            //if (execution.Order != null && (execution.Order.OrderState == OrderState.Filled || execution.Order.OrderState == OrderState.PartFilled))
            //{
            //    if (execution.Order.Name == "Stop loss")
            //    {
            //        MyPrint(execution.Time.ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " @@@@@ L O S E R @@@@@@ OnExecutionUpdate::Stop loss" + " OrderState=" + execution.Order.OrderState.ToString() + " OPEN=" + closedPrice.ToString() + " CLOSE=" + execution.Order.AverageFillPrice.ToString());
            //        MyPrint("---------------------------------------------------------------------------------");

            //        //reset global flags
            //        currPos = Position.posFlat;
            //        profitChasingFlag = false;
            //        stopLossEncountered = true;
            //    }
            //}
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            // Handle entry orders here. The entryOrder object allows us to identify that the order that is calling the OnOrderUpdate() method is the entry order.
            // Assign entryOrder in OnOrderUpdate() to ensure the assignment occurs when expected.
            // This is more reliable than assigning Order objects in OnBarUpdate, as the assignment is not gauranteed to be complete if it is referenced immediately after submitting
            if (order.Name == "Long" || order.Name == "Short")
            {
                entryOrder = order;

                if (order.OrderState == OrderState.Filled || order.OrderState == OrderState.PartFilled)
                {
                    closedPrice = order.AverageFillPrice;
                    if (order.Name == "Long")
                        currPos = Position.posLong;
                    if (order.Name == "Short")
                        currPos = Position.posShort;

                    if (order.Filled == LotSize)
                    {
                        // keep track if a position has been successfully entered, otherwise the position has to be canceled when the next bar arrives
                        MyPrint(defaultErrorType, "OnOrderUpdate, #######Order filled=" + order.Filled + " closedPrice=" + closedPrice + " order name=" + order.Name + " currPos=" + currPos.ToString());
                    }
                    else
                    {
                        // order partially filled
                        MyErrPrint(ErrorType.warning, "OnOrderUpdate, +++++++OrderState.PartFilled, sumFilled=" + order.Filled + ". Need to monitor Order status in Control Center.");
                    }
                }

                // Order cancellation is confirmed by the exchange, cancellation only done manually, therefore will flatten position
                if (order.OrderState == OrderState.Cancelled)
                {
                    MyErrPrint(ErrorType.warning, "OnOrderUpdate, Order cancellation was confirmed by exchange. Will flatten all positions.");
                    FlattenVirtualPositions();    // this will flatten virtual positions and reset all flags
                }

                // Report error and flatten position if new order submissoin rejected, fatal error if closing position rejected
                if (order.OrderState == OrderState.Rejected)
                {
                    if (attemptToFlattenPos) // attempting to close existing positions
                    {
                        MyErrPrint(ErrorType.fatal, "OnOrderUpdate, Closing position order rejected!! May need to manually close position." + " Error code=" + error.ToString() + ": " + nativeError);
                    }
                    else // opening new position rejected
                    {
                        MyErrPrint(ErrorType.fatal, "OnOrderUpdate, New position order rejected!!" + " Error code=" + error.ToString() + ": " + nativeError);
                    }
                    FlattenVirtualPositions();    // this will flatten virtual positions and reset all flags
                }
            }
        }

        private void LogFilesCleanUp()
        {
            if (swLog != null)
            {
                swLog.Close();
                swLog.Dispose();
                swLog = null;
            }

            if (swErr != null)
            {
                swErr.Close();
                swErr.Dispose();
                swErr = null;
            }

            if (swCC != null)
            {
                swCC.Close();
                swCC.Dispose();
                swCC = null;
            }

            if (swCL != null)
            {
                swCL.Close();
                swCL.Dispose();
                swCL = null;
            }

            if (swVIX != null)
            {
                swVIX.Close();
                swVIX.Dispose();
                swVIX = null;
            }

            if (swPstops != null)
            {
                swPstops.Close();
                swPstops.Dispose();
                swPstops = null;
            }
        }

        // Setup the drawdown protections, Pstops and Lstops, VIX >> ADX >> DMR dynamic market range
        private void DailyTradingPolicySetup()
        {
            // Read the current capital file .cc for the current capital, create one if it does not exist
            ReadCurrentCapital();

            // Read current monthly losses file .cl for the current monthly losses, create one if it does not exist
            ReadCurrentMonthlyLosses();
            CheckMonthlyStopLoss();

            // Read current market view file, 0=Bearish, 1=neutral, 2=Bullish
            ReadMarketViewFile();

            // Read the pStops and lStops to set up the profit chasing and stop loss settings
            // this has to be called before ReadEMAVixToSetUpDrawdownSettings(), VIX needs to override this dynamic adjustment
            ReadPstopsnLstopsSettings();

            // Read the 10 days EMA VIX from the VIX file to set up drawdown control settings 
            ReadEMAVixToSetUpDrawdownSettings();

            // This statement needs to be the last statement in real time state so that maxConsecutiveDailyLosses is set after 
            // maxConsecutiveLosses is set in ReadEMAVixToSetUpDrawdownSettings
            SetDailyWinLossState();
        }

        // check if the cumulative P&L or the monthly losses + cumulative P&L is greater than allowable monthly losses, 
        // if greater then set virtualCurrentCapital to zero and halt monthly trading
        private void CheckMonthlyStopLoss()
        {
            double cumulativePL;
            double allowableMonthlyLossesg;

            //SystemPerformance.AllTrades.TradesPerformance.NetProfit keeps track of the P/L only when the system is running
            //i.e. for live trading it only keep tracks of the P/L for the day
            cumulativePL = SystemPerformance.AllTrades.TradesPerformance.NetProfit;
            if (cumulativePL <= 0)
            {
                // the dollar amount allowed for monthly losses depending if monthly profit chasing is met
                if (monthlyProfitChasingFlag)
                    allowableMonthlyLossesg = InitStartingCapital * profitChasingAllowableDrawdown;
                else
                    allowableMonthlyLossesg = InitStartingCapital * maxPercentAllowableDrawdown;

                //Either of the following two conditions could trigger a monthly stop-loss enforcement
                if (Math.Abs(cumulativePL) > allowableMonthlyLossesg)
                {
                    haltTrading = true;

                    // set virtualCurrentCapital to 0 so that it is written into the cc file, no future trading allowed for the month
                    virtualCurrentCapital = 0;

                    //If fatal error, CloseStrategy() is called in MyErrPrint(), which will close all positions and disable strategy
                    MyErrPrint(ErrorType.fatal, "CheckMonthlyStopLoss, !!!!!!!!!!!! Monthly stop loss enforced, Skipping New Trade Position and setting virtualCurrentCapital to ZERO !!!!!!!!!!!!" + " monthlyProfitChasingFlag=" + monthlyProfitChasingFlag);
                    MyPrint(defaultErrorType, "CheckMonthlyStopLoss, virtualCurrentCapital=" + virtualCurrentCapital + " currentMonthlyLosses=" + currentMonthlyLosses + " cumulativePL=" + cumulativePL);
                }
                if (currentMonthlyLosses < 0)
                {
                    if ((Math.Abs(currentMonthlyLosses) + Math.Abs(cumulativePL)) > allowableMonthlyLossesg)
                    {
                        haltTrading = true;

                        // set virtualCurrentCapital to 0 so that it is written into the cc file, no future trading allowed for the month
                        virtualCurrentCapital = 0;

                        //If fatal error, CloseStrategy() is called in MyErrPrint(), which will close all positions and disable strategy
                        MyErrPrint(ErrorType.fatal, "CheckMonthlyStopLoss, !!!!!!!!!!!! Monthly stop loss enforced, Skipping New Trade Position and setting virtualCurrentCapital to ZERO !!!!!!!!!!!!" + " monthlyProfitChasingFlag=" + monthlyProfitChasingFlag);
                        MyPrint(defaultErrorType, "CheckMonthlyStopLoss, virtualCurrentCapital=" + virtualCurrentCapital + " currentMonthlyLosses=" + currentMonthlyLosses + " cumulativePL=" + cumulativePL);
                    }
                }
            }
        }


        // WARNING!!!! Will NOT receive position updates for manually placed orders, or orders managed by other strategies
        protected override void OnPositionUpdate(Cbi.Position position, double averagePrice,
            int quantity, Cbi.MarketPosition marketPosition)
        {
            double totalRealtimePnL = 0;
            double lastTradePnL;


            if (position.MarketPosition == MarketPosition.Flat)
            {
                for (int i = 0; i < SystemPerformance.RealTimeTrades.Count; i++)
                {
                    totalRealtimePnL += SystemPerformance.RealTimeTrades[i].ProfitCurrency;
                }
                lastTradePnL = totalRealtimePnL - lastTotalRealtimePnL;

                // current capital is accurately accounted for when the position is flatten
                virtualCurrentCapital += lastTradePnL;
                lastTotalRealtimePnL = totalRealtimePnL;

                MyPrint(defaultErrorType, "OnPositionUpdate, %%%%%%%%%%%%%%%%%%%%%% Account Positions: Flatten %%%%%%%%%%%%%%%%%%%%%");
                MyPrint(defaultErrorType, "OnPositionUpdate, P&L of last trade= " + lastTradePnL + " virtualCurrentCapital= " + virtualCurrentCapital);
                MyPrint(defaultErrorType, "OnPositionUpdate, %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%");

                CheckMonthlyStopLoss();   // check for monthly stop loss, if stop loss happened, virtualCurrentCapital will be set to zero
                PrintProfitLossCurrentCapital();   // output current virtual capital to cc file
                FlattenVirtualPositions();    // this will flatten virtual positions and reset all flags
            }
            if (position.MarketPosition == MarketPosition.Long)
            {
                MyPrint(defaultErrorType, "OnPositionUpdate, %%%%%%%%%%%%%%%%%%%%%%%% Account Positions: Long %%%%%%%%%%%%%%%%%%%%%%%%");
            }
            if (position.MarketPosition == MarketPosition.Short)
            {
                MyPrint(defaultErrorType, "OnPositionUpdate, %%%%%%%%%%%%%%%%%%%%%%%% Account Positions: Short %%%%%%%%%%%%%%%%%%%%%%%%");
            }
        }


        // Read the current capital file .cc for the current capital, create one if it does not exist
        private void ReadCurrentCapital()
        {
            // read the current capital file .cc for the current capital, create one if it does not exist
            // Create file in the portNumber.cc format, the Path to current capital file, cc file does not have date as part of file name
            pathCC = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "runlog");
            //pathCC = System.IO.Path.Combine(pathCC, Dns.GetHostName() + "-" + portNumber.ToString() + "-" + DateTime.Today.ToString("yyyyMM") + ".cc");
            pathCC = System.IO.Path.Combine(pathCC, hostName + "-" + portNumber.ToString() + "-" + DateTime.Today.ToString("yyyyMM") + ".cc");

            if (File.Exists(pathCC))
            {
                // Read current capital from the cc file
                string ccStr = File.ReadAllText(pathCC);
                virtualCurrentCapital = Convert.ToDouble(ccStr);

                // initializing the monthly control strategy variables with currentCapital from the cc file
                yesterdayVirtualCapital = virtualCurrentCapital; // keep track of capital from previous day
                monthlyProfitChasingFlag = false; // set to false before the month
            }
            MyPrint(defaultErrorType, "ReadCurrentCapital virtualCurrentCapital=" + virtualCurrentCapital);

            swCC = File.CreateText(pathCC); // Open the path for current capital
            swCC.WriteLine(virtualCurrentCapital); // overwrite current capital to cc file, if no existing file, InitStartingCapital will be written as currentCapital
            swCC.Close();
            swCC.Dispose();
            swCC = null;

        }


        // Read the current monthly losses file .cl for the current monthly losses, create one if it does not exist
        private void ReadCurrentMonthlyLosses()
        {
            // read the current monthly losses file .c1 for the current monthly losses, create one if it does not exist
            // Create file in the hostname-portNumber.cl format, the Path to current losses file, cl file does not have date as part of file name
            pathCL = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "runlog");
            //pathCL = System.IO.Path.Combine(pathCL, Dns.GetHostName() + "-" + portNumber.ToString() + "-" + DateTime.Today.ToString("yyyyMM") + ".cl");
            pathCL = System.IO.Path.Combine(pathCL, hostName + "-" + portNumber.ToString() + "-" + DateTime.Today.ToString("yyyyMM") + ".cl");

            if (File.Exists(pathCL))
            {
                // Read current capital from the cc file
                string ccStr = File.ReadAllText(pathCL);
                currentMonthlyLosses = Convert.ToDouble(ccStr);
            }
            MyPrint(defaultErrorType, "ReadCurrentMonthlyLosses currentMonthlyLosses=" + currentMonthlyLosses);

            swCL = File.CreateText(pathCL); // Open the path for current capital
            swCL.WriteLine(currentMonthlyLosses); // overwrite current capital to cc file, if no existing file, InitStartingCapital will be written as currentCapital
            swCL.Close();
            swCL.Dispose();
            swCL = null;
        }


        // Read the 10 days EMA VIX from the VIX file to set up drawdown control settings 
        private void ReadEMAVixToSetUpDrawdownSettings()
        {
            //Read file in the portNumber.cc format, the Path to current vix file, vix file does not have date as part of file name
            pathVIX = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "runlog");
            // VIX is the same across all strategies
            //pathVIX = System.IO.Path.Combine(pathVIX, Dns.GetHostName() + "-" + portNumber.ToString() + ".vix");
            pathVIX = System.IO.Path.Combine(pathVIX, "Artista" + ".vix");

            if (File.Exists(pathVIX))
            {
                double currentVIX;

                string maVIX = File.ReadAllText(pathVIX); // read moving average of VIX

                MyPrint(defaultErrorType, "ReadEMAVixToSetUpDrawdownSettings, maVIX=" + maVIX);

                currentVIX = Convert.ToDouble(maVIX);

                MyPrint(defaultErrorType, "ReadEMAVixToSetUpDrawdownSettings, currentVIX=" + currentVIX);

                // Set monthly and daily drawdown control strategy settings according to moving average VIX read from vix file
                if (currentVIX >= HighVixTreshold)
                {
                    maxConsecutiveLossesUpper = HVmaxConsecutiveLossesUpper;
                    maxConsecutiveLosses = HVmaxConsecutiveLosses;
                    minConsecutiveWins = HVminConsecutiveWins;
                    initMaxConsecutiveLosses = HVmaxConsecutiveLosses;

                    profitChasingTarget = HVprofitChasingTarget; // % monthly gain profit target
                    maxPercentAllowableDrawdown = HVmaxPercentAllowableDrawdown; // allowable maximum % monthly drawdown if profit target did not achieve before trading halt for the month
                    profitChasingAllowableDrawdown = HVprofitChasingAllowableDrawdown;
                }
                else
                {
                    maxConsecutiveLossesUpper = LVmaxConsecutiveLossesUpper;
                    maxConsecutiveLosses = LVmaxConsecutiveLosses;
                    minConsecutiveWins = LVminConsecutiveWins;
                    initMaxConsecutiveLosses = LVmaxConsecutiveLosses;

                    profitChasingTarget = LVprofitChasingTarget; // % monthly gain profit target
                    maxPercentAllowableDrawdown = LVmaxPercentAllowableDrawdown; // allowable maximum % monthly drawdown if profit target did not achieve before trading halt for the month
                    profitChasingAllowableDrawdown = LVprofitChasingAllowableDrawdown;
                }

                MyPrint(defaultErrorType, "ReadEMAVixToSetUpDrawdownSettings, maxConsecutiveLossesUpper=" + maxConsecutiveLossesUpper + " maxConsecutiveLosses=" + maxConsecutiveLosses + " minConsecutiveWins=" + minConsecutiveWins);
                MyPrint(defaultErrorType, "ReadEMAVixToSetUpDrawdownSettings, profitChasingTarget=" + profitChasingTarget + " maxPercentAllowableDrawdown=" + maxPercentAllowableDrawdown + " profitChasingAllowableDrawdown" + profitChasingAllowableDrawdown);
            }
            else
            {
                MyErrPrint(ErrorType.fatal, pathVIX + " VIX file does not exist!");

                //maxConsecutiveLossesUpper = LVmaxConsecutiveLossesUpper;
                //maxConsecutiveLosses = LVmaxConsecutiveLossesUpper;
                //minConsecutiveWins = LVmaxConsecutiveLossesUpper;

                //profitChasingTarget = LVprofitChasingTarget; // % monthly gain profit target
                //maxPercentAllowableDrawdown = LVmaxPercentAllowableDrawdown; // allowable maximum % monthly drawdown if profit target did not achieve before trading halt for the month
                //profitChasingAllowableDrawdown = LVprofitChasingAllowableDrawdown;
            }
        }


        // Read market view file, 0=Bearish, 1=Neutral, 2=Bullish
        private void ReadMarketViewFile()
        {
            int mktView;

            //Read market view file, market view is the same across all strategies on the same machine
            pathMktView = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "runlog");
            pathMktView = System.IO.Path.Combine(pathMktView, "Artista" + ".mkt");

            if (File.Exists(pathMktView))
            {
                string mktViewString = File.ReadAllText(pathMktView); // read market view
                mktView = Convert.ToInt32(mktViewString);

                MyPrint(defaultErrorType, "ReadMarketViewFile, mktViewString=" + mktViewString);

                switch (mktView)
                {
                    case 0:
                        currMarketView = MarketView.Bearish;
                        break;
                    case 2:
                        currMarketView = MarketView.Bullish;
                        break;
                    default:
                        currMarketView = MarketView.Neutral;
                        break;
                }
                MyPrint(defaultErrorType, "ReadMarketViewFile, currMarketView=" + currMarketView.ToString());
            }
            else
            {
                currMarketView = MarketView.Neutral;
                MyErrPrint(ErrorType.warning, pathMktView + " Market View file does not exist! Default currMarketView=" + currMarketView.ToString());
            }
        }


        // Read the pStops and lStops to set up the profit chasing and stop loss settings
        // this has to be called before ReadEMAVixToSetUpDrawdownSettings(), VIX needs to override this dynamic adjustment
        private void ReadPstopsnLstopsSettings()
        {
            //Read pstops file, pstops is the same across all strategies
            pathPstops = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "runlog");
            pathPstops = System.IO.Path.Combine(pathPstops, "Artista" + ".pstop");

            if (File.Exists(pathPstops))
            {
                string pStopsString = File.ReadAllText(pathPstops); // read pStops

                MyPrint(defaultErrorType, "ReadPstopsnLstopsSettings, pStops=" + pStopsString);

                pStops = Convert.ToInt32(pStopsString);
            }
            else
            {
                pStops = defaultPstops;
                MyErrPrint(ErrorType.warning, pathPstops + " Pstops file does not exist! Revert to default pStops=" + pStops);
            }
            lStops = pStops / 2;

            // set profitChasing, softDeck and hardDeck if DMREnabled is TRUE
            if (DMREnabled)
            {
                // set up profit chasing and stop loss settings for the day
                profitChasing = pStops * TicksPerStop; // the target where HandleProfitChasing kicks in
                softDeck = lStops * TicksPerStop; // number of stops for soft stop loss
                hardDeck = pStops * TicksPerStop; //hard deck for auto stop loss
            }
        }


        // CloseStrategy() is called in the event of a fatal error, which will close all positions and disable strategy
        private void MyErrPrint(ErrorType errType, string buf)
        {
            string errString = "";

            if (swErr == null)
            {
                pathErr = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "runlog");
                //pathErr = System.IO.Path.Combine(pathErr, Dns.GetHostName() + "-" + portNumber.ToString() + "-" + DateTime.Today.ToString("yyyyMMdd") + ".err");
                pathErr = System.IO.Path.Combine(pathErr, hostName + "-" + portNumber.ToString() + "-" + DateTime.Today.ToString("yyyyMMdd") + ".err");
                swErr = File.AppendText(pathErr);  // Open the path for err file writing
            }

            switch (errType)
            {
                case ErrorType.fatal:
                    errString = "FATAL: ";
                    haltTrading = true;             // halt trading is it is fatal error
                    break;
                case ErrorType.warning:
                    errString = "WARNING: ";
                    break;
            }

            swErr.WriteLine(errString + DateTime.Now + " " + buf); // Append a new line to the err file

            // close error file
            swErr.Close();
            swErr.Dispose();
            swErr = null;

            MyPrint(errType, errString + DateTime.Now + " " + buf); // replicate error message to log file

            // Cancels all working orders, closes any existing positions, and finally disables the strategy. 
            if (errType == ErrorType.fatal)
            {
                CloseStrategy(errString);
                haltTrading = true;
            }
        }

        private void MyPrint(ErrorType errType, string buf)
        {
            if (swLog == null)
            {
                //Create log file in the portNumber-yyyyMMdd.log format
                pathLog = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "runlog");
                //pathLog = System.IO.Path.Combine(pathLog, Dns.GetHostName() + "-" + portNumber.ToString() + "-" + DateTime.Today.ToString("yyyyMMdd") + ".log");
                pathLog = System.IO.Path.Combine(pathLog, hostName + "-" + portNumber.ToString() + "-" + DateTime.Today.ToString("yyyyMMdd") + ".log");
                swLog = File.AppendText(pathLog);  // Open the path for log file writing
            }

            swLog.WriteLine(DateTime.Now + " " + buf); // Append a new line to the log file

            // only print out verbose, warning and fatal messages to output screen
            if (errType != ErrorType.normal)
            {
                if (errType == ErrorType.warning || errType == ErrorType.verbose)
                    //Set this scripts MyPrint() calls to the first output tab
                    PrintTo = PrintTo.OutputTab1;
                if (errType == ErrorType.fatal)
                    //Set this scripts MyPrint() calls to the second output tab
                    PrintTo = PrintTo.OutputTab2;

                Print(hostName + ":" + portNumber.ToString() + ":" + DateTime.Now + " " + buf);
            }


            swLog.Close();
            swLog.Dispose();
            swLog = null;
        }

        // Account for daily wins and losses for daily drawdown control
        private void SetDailyWinLossState()
        {
            maxConsecutiveDailyLosses = maxConsecutiveLosses;
            consecutiveDailyLosses = 0;
            consecutiveDailyWins = 0;

            MyPrint(defaultErrorType, "SetDailyWinLossState, maxConsecutiveDailyLosses=" + maxConsecutiveDailyLosses + " consecutiveDailyLosses=" + consecutiveDailyLosses + " consecutiveDailyWins=" + consecutiveDailyWins);
        }

        // increment of daily win will increase max consecutive daily losses as long as it does not exceed the upper limit, 
        // it will also reset consecutive daily losses back to zero
        private void IncrementDailyWin()
        {
            consecutiveDailyWins++;
            consecutiveDailyLosses = 0;

            if (consecutiveDailyWins >= minConsecutiveWins)
            {
                if (maxConsecutiveDailyLosses < maxConsecutiveLossesUpper)
                {
                    maxConsecutiveDailyLosses++;
                }
                consecutiveDailyWins = 0;
            }
            MyPrint(defaultErrorType, "IncrementDailyWin, consecutiveDailyWins=" + consecutiveDailyWins + " consecutiveDailyLosses=" + consecutiveDailyLosses);
        }

        private void IncrementDailyLoss()
        {
            consecutiveDailyWins = 0;
            consecutiveDailyLosses++;
            // decrement maxConsecutiveDailyLosses back to initMaxConsecutiveDailyLosses
            if (maxConsecutiveDailyLosses > initMaxConsecutiveLosses)
                maxConsecutiveDailyLosses--;

            MyPrint(defaultErrorType, "IncrementDailyLoss, consecutiveDailyWins=" + consecutiveDailyWins + " consecutiveDailyLosses=" + consecutiveDailyLosses);
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
            EnterShortLimit(LotSize, Bars.GetClose(CurrentBar), "Short");
            MyPrint(defaultErrorType, "AiShort, Server Signal=" + svrSignal + " Short");
        }

        private void AiLong()
        {
            EnterLongLimit(LotSize, Bars.GetClose(CurrentBar), "Long");
            MyPrint(defaultErrorType, "AiLong, Server Signal=" + svrSignal + " Long");
        }

        private void FlattenVirtualPositions()
        {
            currPos = Position.posFlat;
            profitChasingFlag = false;
            attemptToFlattenPos = false;
            profitPercentMet = false; // reset profitPercentMet flag

            MyPrint(defaultErrorType, "FlattenVirtualPositions, currPos=" + currPos + " profitChasingFlag=" + profitChasingFlag + " attemptToFlattenPos=" + attemptToFlattenPos);
        }


        private void AiFlat(ExitOrderType order)
        {
            MyPrint(defaultErrorType, "AiFlat: currPos=" + currPos.ToString() + ", ExitOrderType=" + order);

            if (!PosFlat())
            {
                // attempting to flatten virtual positions, flattening of positions will take place in OnPositionUpdate() callback
                attemptToFlattenPos = true;

                if (PosLong())
                {
                    if (order == ExitOrderType.limit)
                        ExitLongLimit(Bars.GetClose(CurrentBar), "ExitLong", "Long");
                    else
                        ExitLong("ExitLong", "Long");

                    MyPrint(defaultErrorType, "AiFlat, ---------------------------------------------------------------------------------");
                    MyPrint(defaultErrorType, "AiFlat, ExitLong, ExitOrderType=" + order);
                    MyPrint(defaultErrorType, "AiFlat, ---------------------------------------------------------------------------------");
                }
                if (PosShort())
                {
                    if (order == ExitOrderType.limit)
                        ExitShortLimit(Bars.GetClose(CurrentBar), "ExitShort", "Short");
                    else
                        ExitShort("ExitShort", "Short");

                    MyPrint(defaultErrorType, "AiFlat, ---------------------------------------------------------------------------------");
                    MyPrint(defaultErrorType, "AiFlat, ExitShort, ExitOrderType=" + order);
                    MyPrint(defaultErrorType, "AiFlat, ---------------------------------------------------------------------------------");
                }
            }
        }

        private bool IsTradeInterrupted()
        {
            MyPrint(defaultErrorType, "IsTradeInterrupted Checked. VROC(-10/10) && RSI(30/70)");
            MyPrint(defaultErrorType, "closePrice=" + closedPrice + " Close[0]=" + Close[0]);
            MyPrint(defaultErrorType, "profitPercentMet=" + profitPercentMet.ToString());
            MyPrint(defaultErrorType, "SMA9=" + SMA(9)[0].ToString() + " SMA20=" + SMA(20)[0].ToString() + " RSI=" + RSI(14, 3)[0].ToString());
            MyPrint(defaultErrorType, "VROC=" + VROC(25, 3)[0].ToString() + " MACD=" + MACD(12, 26, 9).Diff[0].ToString());
            if (PosLong())
            {
                if ((Close[0] >= (closedPrice + ProfitPercentage * pStops)))  // set profitPercentMet flag if percentage profit target met
                {
                    profitPercentMet = true;
                    MyPrint(defaultErrorType, "IsTradeInterrupted, currPos=" + " >>>>>> 75% Hit >>>>>> ");
                }
                // SMA Exit if ((Close[0] < SMA(SMAConstant)[0]) && profitPercentMet)
                if ((Close[0] < SMA(SMAConstant)[0]) && profitPercentMet)
                {
                    profitPercentMet = false; // reset profitPercentMet flag
                    return true;
                }
            }
            if (PosShort())
            {
                if ((Close[0] <= (closedPrice - ProfitPercentage * pStops))) // set profitPercentMet flag if percentage profit target met
                {
                    profitPercentMet = true;
                    MyPrint(defaultErrorType, "IsTradeInterrupted, currPos=" + " >>>>>> 75% Hit >>>>>> ");
                }
                // SMA Exit if ((Close[0] > SMA(SMAConstant)[0]) && profitPercentMet)
                if ((Close[0] > SMA(SMAConstant)[0]) && profitPercentMet)
                {
                    profitPercentMet = false; // reset profitPercentMet flag
                    return true;
                }
            }
            return false;
        }


        // Skip trade if IsTradeFilered is FALSE
        private bool IsTradeFiltered(string signal)
        {
            // Read current market view file, 0=Bearish, 1=neutral, 2=Bullish
            ReadMarketViewFile();
            MyPrint(defaultErrorType, "Market Outlook = {{{{{ " + currMarketView.ToString() + " }}}}}");

            if (UseRSIAndMACDFilter)
            {
                MyPrint(defaultErrorType, "UseRSIAndMACDFilter");
                MyPrint(defaultErrorType, "RSIUpper=" + RSIUpper.ToString() + " RSILower" + RSILower.ToString() + " RSI=" + RSI(20, 3)[0].ToString());
                MyPrint(defaultErrorType, "MACDDiffThreshold=" + MACDDiffThreshold.ToString() + " MACD_Diff=" + MACD(12, 26, 9).Diff[0].ToString());
                MyPrint(defaultErrorType, "UseRSIAndMACDFilter");
                switch (signal[0])
                {
                    case '0':
                        if (currMarketView == MarketView.Bullish)
                            return false; // eliminate any Sell opportunity
                        // sell
                        if (UseMACDInLessThanMode)
                        {
                            if ((RSI(20, 3)[0] > RSIUpper) && (Math.Abs(MACD(12, 26, 9).Diff[0]) < MACDDiffThreshold))
                                return true;
                        }
                        else
                        {
                            if ((RSI(20, 3)[0] > RSIUpper) && (Math.Abs(MACD(12, 26, 9).Diff[0]) > MACDDiffThreshold))
                                return true;
                        }
                        break;
                    case '2':
                        if (currMarketView == MarketView.Bearish)
                            return false; // eliminate any Buy opportunity
                        // buy
                        if (UseMACDInLessThanMode)
                        {
                            if ((RSI(20, 3)[0] < RSILower) && (Math.Abs(MACD(12, 26, 9).Diff[0]) < MACDDiffThreshold))
                                return true;
                        }
                        else
                        {
                            if ((RSI(20, 3)[0] < RSILower) && (Math.Abs(MACD(12, 26, 9).Diff[0]) > MACDDiffThreshold))
                                return true;
                        }
                        break;
                }
            }
            if (UseRSIFilter)
            {
                MyPrint(defaultErrorType, "UseRSIFilter");
                MyPrint(defaultErrorType, "RSIUpper=" + RSIUpper.ToString() + " RSILower" + RSILower.ToString() + " RSI=" + RSI(20, 3)[0].ToString());
                MyPrint(defaultErrorType, "UseRSIFilter");
                switch (signal[0])
                {
                    case '0':
                        if (currMarketView == MarketView.Bullish)
                            return false; // eliminate any Sell opportunity
                        // sell
                        if (RSI(20, 3)[0] > RSIUpper)
                            return true;
                        break;
                    case '2':
                        if (currMarketView == MarketView.Bearish)
                            return false; // eliminate any Buy opportunity
                        // buy
                        if (RSI(20, 3)[0] < RSILower)
                            return true;
                        break;
                }
            }
            if (UseMACDFilter)
            {
                MyPrint(defaultErrorType, "UseMACDFilter");
                MyPrint(defaultErrorType, "MACDDiffThreshold=" + MACDDiffThreshold.ToString() + " MACD_Diff=" + MACD(12, 26, 9).Diff[0].ToString());
                MyPrint(defaultErrorType, "UseMACDFilter");

                if ((signal[0] == '0') && (currMarketView == MarketView.Bullish))
                    return false; // eliminate any Sell opportunity
                if ((signal[0] == '2') && (currMarketView == MarketView.Bearish))
                    return false; // eliminate any Buy opportunity

                if (UseMACDInLessThanMode)
                {
                    // reject trade until MACD_Diff < MACDDiffThreshold
                    if (Math.Abs(MACD(12, 26, 9).Diff[0]) < MACDDiffThreshold)
                        return true;
                }
                else
                {
                    // reject trade until MACD_Diff > MACDDiffThreshold
                    if (Math.Abs(MACD(12, 26, 9).Diff[0]) > MACDDiffThreshold)
                        return true;
                }
            }
            if (UseCCIFilter)
            {
                MyPrint(defaultErrorType, "UseCCIFilter");
                MyPrint(defaultErrorType, "CCIUpper=" + CCIUpper.ToString() + " CCILower=" + CCILower.ToString() + " CCI=" + CCI(20)[0].ToString());
                MyPrint(defaultErrorType, "UseCCIFilter");
                switch (signal[0])
                {
                    case '0':
                        if (currMarketView == MarketView.Bullish)
                            return false; // eliminate any Sell opportunity
                        // sell
                        if (CCI(20)[0] > CCIUpper)
                            return true;
                        break;
                    case '2':
                        if (currMarketView == MarketView.Bearish)
                            return false; // eliminate any Buy opportunity
                        // buy
                        if (CCI(20)[0] < CCILower)
                            return true;
                        break;
                }
            }
            if (UseADXFilter)
            {
                MyPrint(defaultErrorType, "--- UseADXFilter ---");
                MyPrint(defaultErrorType, "ADXThreshold=" + ADXThreshold.ToString() + " ADX=" + ADX(8)[0].ToString());
                MyPrint(defaultErrorType, "--- UseADXFilter ---");

                if ((signal[0] == '0') && (currMarketView == MarketView.Bullish))
                    return false; // eliminate any Sell opportunity
                if ((signal[0] == '2') && (currMarketView == MarketView.Bearish))
                    return false; // eliminate any Buy opportunity

                if (ADX(8)[0] > ADXThreshold)
                    return true;
            }
            if (UseVROCFilter)
            {
                MyPrint(defaultErrorType, "--- UseVROCFilter ---");
                MyPrint(defaultErrorType, "VROCUpper=" + VROCUpper.ToString() + " VROCPos=" + VROCPos.ToString() + " VROCNeg=" + VROCNeg.ToString() + " VROC=" + VROC(25, 3)[0].ToString()); MyPrint(defaultErrorType, "--- UseVROCFilter ---");
                switch (signal[0])
                {
                    case '0':
                        if (currMarketView == MarketView.Bullish)
                            return false; // eliminate any Sell opportunity
                        // sell
                        //if (VROC(25, 3)[0] < VROCLower)
                        //    return true;
                        // Sell when VROC is in range [VROCNeg..VROCPos]
                        if ((VROC(25, 3)[0] < VROCPos) && (VROC(25, 3)[0] > VROCNeg))
                            return true;
                        break;
                    case '2':
                        if (currMarketView == MarketView.Bearish)
                            return false; // eliminate any Buy opportunity
                        // buy
                        // Bullish vs Neutral market outlook VROC entry for Buy
                        if (currMarketView == MarketView.Bullish)
                        {
                            if ((VROC(25, 3)[0] > VROCUpper) || ((VROC(25, 3)[0] < VROCPos) && (VROC(25, 3)[0] > VROCNeg)))
                                return true;
                        }
                        else // Neutral
                        {
                            if ((VROC(25, 3)[0] > VROCUpper))
                                return true;
                        }
                        break;
                }
            }
            if (UserMACDAndVROC)
            {
                MyPrint(defaultErrorType, "--- UseMACDAndVROC ---");
                MyPrint(defaultErrorType, "VROCUpper=" + VROCUpper.ToString() + " VROCLower" + VROCLower.ToString() + " VROC=" + VROC(25, 3)[0].ToString());
                MyPrint(defaultErrorType, "MACDDiffThreshold=" + MACDDiffThreshold.ToString() + " MACD_Diff=" + MACD(12, 26, 9).Diff[0].ToString());
                MyPrint(defaultErrorType, "--- UseMACDAndVROC ---");

                switch (signal[0])
                {
                    case '0':
                        if (currMarketView == MarketView.Bullish)
                            return false; // eliminate any Sell opportunity
                        // sell
                        if (UseMACDInLessThanMode)
                        {
                            if ((VROC(25, 3)[0] < VROCLower) && (Math.Abs(MACD(12, 26, 9).Diff[0]) < MACDDiffThreshold))
                                return true;
                        }
                        else
                        {
                            if ((VROC(25, 3)[0] < VROCLower) && (Math.Abs(MACD(12, 26, 9).Diff[0]) > MACDDiffThreshold))
                                return true;
                        }
                        break;
                    case '2':
                        if (currMarketView == MarketView.Bearish)
                            return false; // eliminate any Buy opportunity
                        // buy
                        if (UseMACDInLessThanMode)
                        {
                            if ((VROC(25, 3)[0] > VROCUpper) && (Math.Abs(MACD(12, 26, 9).Diff[0]) < MACDDiffThreshold))
                                return true;
                        }
                        else
                        {
                            if ((VROC(25, 3)[0] > VROCUpper) && (Math.Abs(MACD(12, 26, 9).Diff[0]) > MACDDiffThreshold))
                                return true;
                        }
                        break;
                }
            }
            if (UseRSIAndVROCFilter)
            {
                MyPrint(defaultErrorType, "--- UseRSIAndVROCFilter ---");
                MyPrint(defaultErrorType, "VROCUpper=" + VROCUpper.ToString() + " VROCLower" + VROCLower.ToString() + " VROC=" + VROC(25, 3)[0].ToString());
                MyPrint(defaultErrorType, "RSIUpper=" + RSIUpper.ToString() + " RSILower" + RSILower.ToString() + " RSI=" + RSI(20, 3)[0].ToString());
                MyPrint(defaultErrorType, "--- UseRSIAndVROCFilter ---");
                switch (signal[0])
                {
                    case '0':
                        if (currMarketView == MarketView.Bullish)
                            return false; // eliminate any Sell opportunity
                        // sell
                        if ((RSI(20, 3)[0] > RSIUpper) && (VROC(25, 3)[0] < VROCLower))
                            return true;
                        break;
                    case '2':
                        if (currMarketView == MarketView.Bearish)
                            return false; // eliminate any Buy opportunity
                        // buy
                        if ((RSI(20, 3)[0] < RSILower) && (VROC(25, 3)[0] > VROCUpper))
                            return true;
                        break;
                }
            }
            if (UseADXAndVROCFilter)
            {
                MyPrint(defaultErrorType, "--- UseADXAndVROCFilter ---");
                MyPrint(defaultErrorType, "VROCUpper=" + VROCUpper.ToString() + " VROCLower" + VROCLower.ToString() + " VROC=" + VROC(25, 3)[0].ToString());
                MyPrint(defaultErrorType, "ADXThreshold=" + ADXThreshold.ToString() + " ADX=" + ADX(8)[0].ToString());
                MyPrint(defaultErrorType, "--- UseADXAndVROCFilter ---");

                switch (signal[0])
                {
                    case '0':
                        if (currMarketView == MarketView.Bullish)
                            return false; // eliminate any Sell opportunity
                        // sell
                        if ((ADX(8)[0] > ADXThreshold) && (VROC(25, 3)[0] < VROCLower))
                            return true;
                        break;
                    case '2':
                        if (currMarketView == MarketView.Bearish)
                            return false; // eliminate any Buy opportunity
                        // buy
                        if ((ADX(8)[0] > ADXThreshold) && (VROC(25, 3)[0] > VROCUpper))
                            return true;
                        break;
                }
            }
            if (UseRSIADXandVROCFilter)
            {
                MyPrint(defaultErrorType, "--- UseRSIADXandVROCFilter ---");
                MyPrint(defaultErrorType, "VROCUpper=" + VROCUpper.ToString() + " VROCLower" + VROCLower.ToString() + " VROC=" + VROC(25, 3)[0].ToString());
                MyPrint(defaultErrorType, "ADXThreshold=" + ADXThreshold.ToString() + " ADX=" + ADX(8)[0].ToString());
                MyPrint(defaultErrorType, "RSIUpper=" + RSIUpper.ToString() + " RSILower" + RSILower.ToString() + " RSI=" + RSI(20, 3)[0].ToString());
                MyPrint(defaultErrorType, "--- UseRSIADXandVROCFilter ---");

                switch (signal[0])
                {
                    case '0':
                        if (currMarketView == MarketView.Bullish)
                            return false; // eliminate any Sell opportunity
                        // sell
                        if ((ADX(8)[0] > ADXThreshold) && (VROC(25, 3)[0] < VROCLower) && (RSI(20, 3)[0] > RSIUpper))
                            return true;
                        break;
                    case '2':
                        if (currMarketView == MarketView.Bearish)
                            return false; // eliminate any Buy opportunity
                        // buy
                        if ((ADX(8)[0] > ADXThreshold) && (VROC(25, 3)[0] > VROCUpper) && (RSI(20, 3)[0] < RSILower))
                            return true;
                        break;
                }
            }
            if (UseMACDorRSIandVROCFilter)
            {
                {
                    MyPrint(defaultErrorType, "UseMACDorRSIandVROCFilter");
                    MyPrint(defaultErrorType, "RSIUpper=" + RSIUpper.ToString() + " RSILower" + RSILower.ToString() + " RSI=" + RSI(20, 3)[0].ToString());
                    MyPrint(defaultErrorType, "MACDDiffThreshold=" + MACDDiffThreshold.ToString() + " MACD_Diff=" + MACD(12, 26, 9).Diff[0].ToString());
                    MyPrint(defaultErrorType, "VROCUpper=" + VROCUpper.ToString() + " VROCLower" + VROCLower.ToString() + " VROC=" + VROC(25, 3)[0].ToString());
                    MyPrint(defaultErrorType, "UseMACDorRSIandVROCFilter");
                    switch (signal[0])
                    {
                        case '0':
                            if (currMarketView == MarketView.Bullish)
                                return false; // eliminate any Sell opportunity
                            // sell
                            if (UseMACDInLessThanMode)
                            {
                                // reject trade unless VROC AND (MACD OR RSI) passed
                                if ((VROC(25, 3)[0] < VROCLower) && ((RSI(20, 3)[0] > RSIUpper) || (Math.Abs(MACD(12, 26, 9).Diff[0]) < MACDDiffThreshold)))
                                    return true;
                            }
                            else
                            {
                                // reject trade unless VROC AND (MACD OR RSI) passed
                                if ((VROC(25, 3)[0] < VROCLower) && ((RSI(20, 3)[0] > RSIUpper) || (Math.Abs(MACD(12, 26, 9).Diff[0]) > MACDDiffThreshold)))
                                    return true;
                            }
                            break;
                        case '2':
                            if (currMarketView == MarketView.Bearish)
                                return false; // eliminate any Buy opportunity
                            // buy
                            if (UseMACDInLessThanMode)
                            {
                                // reject trade unless VROC AND (MACD OR RSI) passed
                                if ((VROC(25, 3)[0] > VROCUpper) && ((RSI(20, 3)[0] < RSILower) || (Math.Abs(MACD(12, 26, 9).Diff[0]) < MACDDiffThreshold)))
                                    return true;
                            }
                            else
                            {
                                // reject trade unless VROC AND (MACD OR RSI) passed
                                if ((VROC(25, 3)[0] > VROCUpper) && ((RSI(20, 3)[0] < RSILower) || (Math.Abs(MACD(12, 26, 9).Diff[0]) > MACDDiffThreshold)))
                                    return true;
                            }
                            break;
                    }
                }
            }
            return false;
        }


        // starting a new trade position by submitting an order to the brokerage, OnOrderUpdate callback will reflect the state of the order submitted
        private void StartNewTradePosition(string signal)
        {
            // Attempting to start new trade while flattening current position, will not start new trade
            if (attemptToFlattenPos)
            {
                MyErrPrint(ErrorType.warning, "StartNewTradePosition, Attempting to enter new trade while flattening current position, will not start new trade. Check exit order status.");
                return;
            }
            // Attempting to start new trade while current order partially filled, will not start new trade
            if ((entryOrder != null) && (entryOrder.OrderState == OrderState.PartFilled))
            {
                MyErrPrint(ErrorType.warning, "StartNewTradePosition, Attempting to enter new trade while current order partially filled, will not start new trade. Check current order status.");
                return;
            }
            // Current order is in Submitted, Accepted or Working state, will not start new trade
            if ((entryOrder != null) && (entryOrder.OrderState == OrderState.Submitted || entryOrder.OrderState == OrderState.Accepted || entryOrder.OrderState == OrderState.Working))
            {
                MyErrPrint(ErrorType.warning, "StartNewTradePosition, Attempting to enter new trade while current order status is " + entryOrder.OrderState.ToString() + ",  will not start new trade. Check current order status.");
                return;
            }
            // Monrhly stop loss enfoced, will not start new trade
            if (virtualCurrentCapital == 0)
            {
                MyErrPrint(ErrorType.fatal, "StartNewTradePosition, Will NOT start new trade while Virtual Current Capital is " + virtualCurrentCapital + " Monthly Stop Loss enforced!");
                return;
            }

            // Skip trade if IsTradeFilered is FALSE
            if (UseEntryFilter && !IsTradeFiltered(signal))
            {
                MyPrint(defaultErrorType, "IsTradeFiltered failed! NO TRADE!");
                return;
            }

            switch (signal[0])
            {
                case '0':
                    // sell
                    MyPrint(defaultErrorType, "StartNewTradePosition, Server signal=" + signal);
                    AiShort();
                    break;
                case '2':
                    // buy
                    MyPrint(defaultErrorType, "StartNewTradePosition, Server signal=" + signal);
                    AiLong();
                    break;
                default:
                    // do nothing if signal is 1 for flat position
                    break;
            }
        }

        // Will stop trades from proceeding if some conditions are met, e.g. daily stop loss met
        private void ExecuteAITrade(string signal)
        {
            MyPrint(defaultErrorType, "ExecuteAITrade, haltTrading=" + haltTrading + " attemptToFlattenPos=" + attemptToFlattenPos + " State=" + State.ToString());

            // don't start new trade if not real time, halt trading or attempting to flatten positions
            if ((State != State.Realtime) || haltTrading || attemptToFlattenPos)
                return;

            MyPrint(defaultErrorType, "ExecuteAITrade, consecutiveDailyLosses=" + consecutiveDailyLosses + " maxConsecutiveDailyLosses=" + maxConsecutiveDailyLosses);
            // don't execute trade if consecutive losses greater than allowable limits
            if (consecutiveDailyLosses >= maxConsecutiveDailyLosses)
            {
                //If fatal error, CloseStrategy() is called in MyErrPrint(), which will close all positions and disable strategy
                MyErrPrint(ErrorType.fatal, "ExecuteAITrade, consecutiveDailyLosses >= maxConsecutiveDailyLosses, Halt trading enforced, skipping StartNewTradePosition");
                //haltTrading = true;
                return;
            }

            // Set monthlyProfitChasingFlag, once monthlyProfitChasingFlag sets to true, it will stay true until end of the month
            if (!monthlyProfitChasingFlag)
            {
                MyPrint(defaultErrorType, "ExecuteAITrade, virtualCurrentCapital=" + virtualCurrentCapital + " InitStartingCapital=" + InitStartingCapital + " profitChasingTarget=" + profitChasingTarget);
                if (virtualCurrentCapital > (InitStartingCapital * (1 + profitChasingTarget)))
                {
                    MyPrint(defaultErrorType, "ExecuteAITrade, $$$$$$$$$$$$$ Monthly profit target met, Monthly Profit Chasing and Stop Loss begins! $$$$$$$$$$$$$");
                    monthlyProfitChasingFlag = true;
                }
            }

            //MyPrint("ExecuteAITrade");
            if (PosFlat())
            {
                StartNewTradePosition(signal);
                return;
            }
        }


        // Exit current positions if market dynamic shifted against current positions
        private void HandleMarketShift()
        {
            double estVirtualCurrentCapital;

            if (PosFlat())
            {
                // this is not possible
                Debug.Assert(!PosFlat(), "ASSERT: Position is flat while HandleMarketShift");
                return;
            }

            if (PosLong())
            {
                // Exit position if IsTradeFilered is TRUE
                if (IsTradeInterrupted())
                {
                    //MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " HandleSoftDeck:: signal= " + signal.ToString() + " current price=" + Close[0] + " closedPrice=" + closedPrice.ToString() + " soft deck=" + (softDeck * TickSize).ToString() + " @@@@@ L O S E R @@@@@@ loss= " + (Close[0]-closedPrice).ToString());
                    MyPrint(defaultErrorType, "");
                    MyPrint(defaultErrorType, "HandleMarketShift," + " OPEN=" + closedPrice.ToString() + " CLOSE=" + Close[0] + " soft deck=" + (softDeck * TickSize).ToString() + " @@@@@ EARLY EXIT @@@@@@ loss= " + ((Close[0] - closedPrice) * dollarValPerPoint - CommissionRate).ToString());
                    MyPrint(defaultErrorType, "");
                    AiFlat(ExitOrderType.limit);

                    // if early exit is a loss then increment daily losses count
                    if (((Close[0] - closedPrice) * dollarValPerPoint - CommissionRate) < 0)
                        IncrementDailyLoss();
                    else
                        IncrementDailyWin();

                    // keeping records for monthly profit chasing and stop loss strategy
                    // estCurrentCapital is an estimate because time lagged between AiFlat() and actual closing of account position
                    estVirtualCurrentCapital = virtualCurrentCapital + ((Close[0] - closedPrice) * dollarValPerPoint - CommissionRate);

                    // stop trading if monthly profit is met and trading going negative
                    if (monthlyProfitChasingFlag && (estVirtualCurrentCapital < yesterdayVirtualCapital))
                    {
                        MyPrint(defaultErrorType, "HandleMarketShift, monthlyProfitChasingFlag=" + monthlyProfitChasingFlag + " estVirtualCurrentCapital=" + estVirtualCurrentCapital.ToString() + " yesterdayVirtualCapital=" + yesterdayVirtualCapital.ToString() + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartNewTradePosition $$$$$$$!!!!!!!!");
                        haltTrading = true;

                        // set virtualCurrentCapital to 0 so that it is written into the cc file, no future trading allowed for the month
                        virtualCurrentCapital = 0;
                        PrintProfitLossCurrentCapital();   // output current virtual capital to cc file
                    }

                }
                return;
            }

            if (PosShort())
            {
                // Exit position if IsTradeFilered is TRUE
                if (IsTradeInterrupted())
                {
                    //MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " HandleSoftDeck:: signal= " + signal.ToString() + " current price=" + Close[0] + " closedPrice=" + closedPrice.ToString() + " soft deck=" + (softDeck * TickSize).ToString() + " @@@@@ L O S E R @@@@@@ loss= " + (closedPrice- Close[0]).ToString());
                    MyPrint(defaultErrorType, "");
                    MyPrint(defaultErrorType, "HandleMarketShift," + " OPEN=" + closedPrice.ToString() + " CLOSE=" + Close[0] + " soft deck=" + (softDeck * TickSize).ToString() + " @@@@@ EARLY EXIT @@@@@@ loss= " + ((closedPrice - Close[0]) * dollarValPerPoint - CommissionRate).ToString());
                    MyPrint(defaultErrorType, "");
                    AiFlat(ExitOrderType.limit);

                    // if early exit is a loss then increment daily losses count
                    if (((closedPrice - Close[0]) * dollarValPerPoint - CommissionRate) < 0)
                        IncrementDailyLoss();
                    else
                        IncrementDailyWin();

                    // keeping records for monthly profit chasing and stop loss strategy
                    // estCurrentCapital is an estimate because time lagged between AiFlat() and actual closing of account position
                    estVirtualCurrentCapital = virtualCurrentCapital + ((closedPrice - Close[0]) * dollarValPerPoint - CommissionRate);

                    // stop trading if monthly profit is met and trading going negative
                    if (monthlyProfitChasingFlag && (estVirtualCurrentCapital < yesterdayVirtualCapital))
                    {
                        MyPrint(defaultErrorType, "HandleMarketShift, monthlyProfitChasingFlag=" + monthlyProfitChasingFlag + " estCurrentVirtualCapital=" + estVirtualCurrentCapital.ToString() + " yesterdayVirtualCapital=" + yesterdayVirtualCapital.ToString() + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartNewTradePosition $$$$$$$!!!!!!!!");
                        haltTrading = true;

                        // set virtualCurrentCapital to 0 so that it is written into the cc file, no future trading allowed for the month
                        virtualCurrentCapital = 0;
                        PrintProfitLossCurrentCapital();   // output current virtual capital to cc file
                    }
                }

                return;
            }
        }


        private void HandleSoftDeck(string signal)
        {
            double estVirtualCurrentCapital;

            if (PosFlat())
            {
                // this is not possible
                Debug.Assert(!PosFlat(), "ASSERT: Position is flat while HandleSoftDeck");
                return;
            }

            if (PosLong())
            {
                if (signal[0] != '2')
                {
                    //MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " HandleSoftDeck:: signal= " + signal.ToString() + " current price=" + Close[0] + " closedPrice=" + closedPrice.ToString() + " soft deck=" + (softDeck * TickSize).ToString() + " @@@@@ L O S E R @@@@@@ loss= " + (Close[0]-closedPrice).ToString());
                    MyPrint(defaultErrorType, "");
                    MyPrint(defaultErrorType, "HandleSoftDeck, signal= " + signal.ToString() + " OPEN=" + closedPrice.ToString() + " CLOSE=" + Close[0] + " soft deck=" + (softDeck * TickSize).ToString() + " @@@@@ L O S E R @@@@@@ loss= " + ((Close[0] - closedPrice) * dollarValPerPoint - CommissionRate).ToString());
                    MyPrint(defaultErrorType, "");
                    AiFlat(ExitOrderType.limit);

                    IncrementDailyLoss();

                    // keeping records for monthly profit chasing and stop loss strategy
                    // estCurrentCapital is an estimate because time lagged between AiFlat() and actual closing of account position
                    estVirtualCurrentCapital = virtualCurrentCapital + ((Close[0] - closedPrice) * dollarValPerPoint - CommissionRate);

                    // stop trading if monthly profit is met and trading going negative
                    if (monthlyProfitChasingFlag && (estVirtualCurrentCapital < yesterdayVirtualCapital))
                    {
                        MyPrint(defaultErrorType, "HandleSoftDeck, monthlyProfitChasingFlag=" + monthlyProfitChasingFlag + " estVirtualCurrentCapital=" + estVirtualCurrentCapital.ToString() + " yesterdayVirtualCapital=" + yesterdayVirtualCapital.ToString() + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartNewTradePosition $$$$$$$!!!!!!!!");
                        haltTrading = true;

                        // set virtualCurrentCapital to 0 so that it is written into the cc file, no future trading allowed for the month
                        virtualCurrentCapital = 0;
                        PrintProfitLossCurrentCapital();   // output current virtual capital to cc file
                    }
                }
                return;
            }

            if (PosShort())
            {
                if (signal[0] != '0')
                {
                    //MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " HandleSoftDeck:: signal= " + signal.ToString() + " current price=" + Close[0] + " closedPrice=" + closedPrice.ToString() + " soft deck=" + (softDeck * TickSize).ToString() + " @@@@@ L O S E R @@@@@@ loss= " + (closedPrice- Close[0]).ToString());
                    MyPrint(defaultErrorType, "");
                    MyPrint(defaultErrorType, "HandleSoftDeck, signal= " + signal.ToString() + " OPEN=" + closedPrice.ToString() + " CLOSE=" + Close[0] + " soft deck=" + (softDeck * TickSize).ToString() + " @@@@@ L O S E R @@@@@@ loss= " + ((closedPrice - Close[0]) * dollarValPerPoint - CommissionRate).ToString());
                    MyPrint(defaultErrorType, "");
                    AiFlat(ExitOrderType.limit);

                    IncrementDailyLoss();

                    // keeping records for monthly profit chasing and stop loss strategy
                    // estCurrentCapital is an estimate because time lagged between AiFlat() and actual closing of account position
                    estVirtualCurrentCapital = virtualCurrentCapital + ((closedPrice - Close[0]) * dollarValPerPoint - CommissionRate);

                    // stop trading if monthly profit is met and trading going negative
                    if (monthlyProfitChasingFlag && (estVirtualCurrentCapital < yesterdayVirtualCapital))
                    {
                        MyPrint(defaultErrorType, "HandleSoftDeck, monthlyProfitChasingFlag=" + monthlyProfitChasingFlag + " estCurrentVirtualCapital=" + estVirtualCurrentCapital.ToString() + " yesterdayVirtualCapital=" + yesterdayVirtualCapital.ToString() + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartNewTradePosition $$$$$$$!!!!!!!!");
                        haltTrading = true;

                        // set virtualCurrentCapital to 0 so that it is written into the cc file, no future trading allowed for the month
                        virtualCurrentCapital = 0;
                        PrintProfitLossCurrentCapital();   // output current virtual capital to cc file
                    }
                }
                return;
            }
        }

        private bool ViolateSoftDeck()
        {
            if (PosLong())
            {
                return (Bars.GetClose(CurrentBar) <= (closedPrice - softDeck * TickSize));
            }
            if (PosShort())
            {
                return (Bars.GetClose(CurrentBar) >= (closedPrice + softDeck * TickSize));
            }
            return false;
        }

        private void HandleHardDeck()
        {
            double estVirtualCurrentCapital;

            if (PosFlat())
            {
                // this is not possible
                Debug.Assert(!PosFlat(), "ASSERT: Position is flat while HandleHardDeck");
                return;
            }

            if (PosLong())
            {
                //If fatal error, CloseStrategy() is called in MyErrPrint(), which will close all positions and disable strategy
                MyErrPrint(ErrorType.fatal, "HandleHardDeck, Confirmation of position flatten needed. OPEN=" + closedPrice.ToString() + " CLOSE=" + Close[0] + " @@@@@ L O S E R @@@@@@ loss= " + ((Close[0] - closedPrice) * dollarValPerPoint - CommissionRate).ToString());
                //AiFlat(ExitOrderType.market);

                IncrementDailyLoss();

                // keeping records for monthly profit chasing and stop loss strategy
                // estCurrentCapital is an estimate because time lagged between AiFlat() and actual closing of account position
                estVirtualCurrentCapital = virtualCurrentCapital + ((Close[0] - closedPrice) * dollarValPerPoint - CommissionRate);

                // stop trading if monthly profit is met and trading going negative
                if (monthlyProfitChasingFlag && (estVirtualCurrentCapital < yesterdayVirtualCapital))
                {
                    MyPrint(defaultErrorType, "HandleHardDeck, monthlyProfitChasingFlag=" + monthlyProfitChasingFlag + "estVirtualCurrentCapital=" + estVirtualCurrentCapital.ToString() + " yesterdayVirtualCapital=" + yesterdayVirtualCapital.ToString() + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartNewTradePosition $$$$$$$!!!!!!!!");
                    haltTrading = true;

                    // set virtualCurrentCapital to 0 so that it is written into the cc file, no future trading allowed for the month
                    virtualCurrentCapital = 0;
                    PrintProfitLossCurrentCapital();   // output current virtual capital to cc file
                }
            }

            if (PosShort())
            {
                //If fatal error, CloseStrategy() is called in MyErrPrint(), which will close all positions and disable strategy
                MyErrPrint(ErrorType.fatal, "HandleHardDeck,  Confirmation of position flatten needed. OPEN=" + closedPrice.ToString() + " CLOSE=" + Close[0] + " @@@@@ L O S E R @@@@@@ loss= " + ((closedPrice - Close[0]) * dollarValPerPoint - CommissionRate).ToString());
                //CloseStrategy() called in MyErrPrint when error is fatal, it will flatten all positions
                //AiFlat(ExitOrderType.market);

                IncrementDailyLoss();

                // keeping records for monthly profit chasing and stop loss strategy
                // estCurrentCapital is an estimate because time lagged between AiFlat() and actual closing of account position
                estVirtualCurrentCapital = virtualCurrentCapital + ((closedPrice - Close[0]) * dollarValPerPoint - CommissionRate);

                // stop trading if monthly profit is met and trading going negative
                if (monthlyProfitChasingFlag && (estVirtualCurrentCapital < yesterdayVirtualCapital))
                {
                    MyPrint(defaultErrorType, "HandleHardDeck, monthlyProfitChasingFlag=" + monthlyProfitChasingFlag + "estVirtualCurrentCapital=" + estVirtualCurrentCapital.ToString() + " yesterdayVirtualCapital=" + yesterdayVirtualCapital.ToString() + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartNewTradePosition $$$$$$$!!!!!!!!");
                    haltTrading = true;

                    // set virtualCurrentCapital to 0 so that it is written into the cc file, no future trading allowed for the month
                    virtualCurrentCapital = 0;
                    PrintProfitLossCurrentCapital();   // output current virtual capital to cc file
                }
            }
        }

        private bool ViolateHardDeck()
        {
            if (PosLong())
            {
                return (Close[0] <= (closedPrice - hardDeck * TickSize));
            }
            if (PosShort())
            {
                return (Close[0] >= (closedPrice + hardDeck * TickSize));
            }
            return false;
        }

        private void HandleProfitChasing(string signal)
        {
            double estVirtualCurrentCapital;

            if (PosFlat())
            {
                // this is not possible
                Debug.Assert(!PosFlat(), "ASSERT: Position is flat while HandleProfitChasing");
                return;
            }
            // if market trend go against profit positions and server signal against current position, then flatten position and take profits
            if (PosLong())
            {
                if (Bars.GetClose(CurrentBar) < Bars.GetClose(CurrentBar - 1) && signal[0] == '0')
                {
                    //MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " HandleProfitChasing::" + " currPos=" + currPos.ToString() + " closedPrice=" + closedPrice.ToString() + " Close[0]=" + Close[0].ToString() + " closedPrice + profitChasing=" + (closedPrice + profitChasing * TickSize).ToString() + " >>>>>> W I N N E R >>>>>> Profits= " + (Close[0] - closedPrice).ToString());
                    MyPrint(defaultErrorType, "");
                    MyPrint(defaultErrorType, "HandleProfitChasing, currPos=" + currPos + " OPEN=" + closedPrice + " CLOSE=" + Close[0] + " >>>>>> W I N N E R >>>>>> Profits= " + ((Close[0] - closedPrice) * dollarValPerPoint - CommissionRate));
                    MyPrint(defaultErrorType, "");
                    AiFlat(ExitOrderType.limit);

                    IncrementDailyWin();

                    // keeping records for monthly profit chasing and stop loss strategy
                    // currentCapital is an estimate because time lagged between AiFlat() and actual closing of account position
                    estVirtualCurrentCapital = virtualCurrentCapital + ((Close[0] - closedPrice) * dollarValPerPoint - CommissionRate);

                    // stop trading if monthly profit is met and trading going negative
                    if (monthlyProfitChasingFlag && (estVirtualCurrentCapital < yesterdayVirtualCapital))
                    {
                        MyPrint(defaultErrorType, "HandleProfitChasing, monthlyProfitChasingFlag=" + monthlyProfitChasingFlag + "estVirtualCurrentCapital=" + estVirtualCurrentCapital + " yesterdayVirtualCapital=" + yesterdayVirtualCapital + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartNewTradePosition $$$$$$$!!!!!!!!");
                        haltTrading = true;

                        // set virtualCurrentCapital to 0 so that it is written into the cc file, no future trading allowed for the month
                        virtualCurrentCapital = 0;
                        PrintProfitLossCurrentCapital();   // output current virtual capital to cc file
                        MyErrPrint(ErrorType.fatal, "HandleProfitChasing, monthlyProfitChasingFlag=" + monthlyProfitChasingFlag + "estVirtualCurrentCapital=" + estVirtualCurrentCapital + " yesterdayVirtualCapital=" + yesterdayVirtualCapital + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartNewTradePosition $$$$$$$!!!!!!!!");
                    }
                }
            }
            if (PosShort())
            {
                if (Bars.GetClose(CurrentBar) > Bars.GetClose(CurrentBar - 1) && signal[0] == '2')
                {
                    MyPrint(defaultErrorType, "");
                    MyPrint(defaultErrorType, "HandleProfitChasing, currPos=" + currPos.ToString() + " OPEN=" + closedPrice.ToString() + " CLOSE=" + Close[0].ToString() + " >>>>>> W I N N E R >>>>>> Profits= " + ((closedPrice - Close[0]) * dollarValPerPoint - CommissionRate).ToString());
                    MyPrint(defaultErrorType, "");
                    AiFlat(ExitOrderType.limit);

                    IncrementDailyWin();

                    // keeping records for monthly profit chasing and stop loss strategy
                    // estCurrentCapital is an estimate because time lagged between AiFlat() and actual closing of account position
                    estVirtualCurrentCapital = virtualCurrentCapital + ((closedPrice - Close[0]) * dollarValPerPoint - CommissionRate);

                    // stop trading if monthly profit is met and trading going negative
                    if (monthlyProfitChasingFlag && (estVirtualCurrentCapital < yesterdayVirtualCapital))
                    {
                        MyPrint(defaultErrorType, "HandleProfitChasing, monthlyProfitChasingFlag=" + monthlyProfitChasingFlag + "estVirtualCurrentCapital=" + estVirtualCurrentCapital + " yesterdayVirtualCapital=" + yesterdayVirtualCapital + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartNewTradePosition $$$$$$$!!!!!!!!");
                        haltTrading = true;

                        // set virtualCurrentCapital to 0 so that it is written into the cc file, no future trading allowed for the month
                        virtualCurrentCapital = 0;
                        PrintProfitLossCurrentCapital();   // output current virtual capital to cc file
                        MyErrPrint(ErrorType.fatal, "HandleProfitChasing, monthlyProfitChasingFlag=" + monthlyProfitChasingFlag + "estVirtualCurrentCapital=" + estVirtualCurrentCapital.ToString() + " yesterdayVirtualCapital=" + yesterdayVirtualCapital.ToString() + " $$$$$$$!!!!!!!! Monthly profit target met, stop loss enforced, Skipping StartNewTradePosition $$$$$$$!!!!!!!!");
                    }
                }
            }
        }

        private bool TouchedProfitChasing()
        {
            profitChasingFlag = false;

            if (PosLong())
            {
                //if (Close[0] >= (closedPrice + profitChasing * TickSize))
                if (Bars.GetClose(CurrentBar) >= (closedPrice + profitChasing * TickSize))
                {
                    MyPrint(defaultErrorType, "TouchedProfitChasing <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<==================================");
                    profitChasingFlag = true;
                    return profitChasingFlag;
                }
            }
            if (PosShort())
            {
                //if (Close[0] <= (closedPrice - profitChasing * TickSize))
                if (Bars.GetClose(CurrentBar) <= (closedPrice - profitChasing * TickSize))
                {
                    MyPrint(defaultErrorType, "TouchedProfitChasing <<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<<==================================");
                    profitChasingFlag = true;
                    return profitChasingFlag;
                }
            }

            return profitChasingFlag;
        }

        private void CloseCurrentPositions()
        {
            // Flattening position already in progress
            if (attemptToFlattenPos)
                return;

            // EOD close current position(s)
            MyPrint(defaultErrorType, "CloseCurrentPositions, HandleEOD:: " + " current price=" + Close[0] + " closedPrice=" + closedPrice.ToString() + " Close[0]=" + Close[0].ToString() + " P/L= " + ((Close[0] - closedPrice) * dollarValPerPoint - CommissionRate).ToString());
            AiFlat(ExitOrderType.limit);
        }

        private void ResetServer()
        {
            //CloseCurrentPositions();

            string resetString = "-1";
            byte[] resetMsg = Encoding.UTF8.GetBytes(resetString);

            // Send reset string of "-1" to the server  
            int resetSent = sender.Send(resetMsg);

            lineNo = 0;
        }

        private void PrintProfitLossCurrentCapital()
        {
            //SystemPerformance.AllTrades.TradesPerformance.NetProfit keeps track of the P/L only when the system is running
            //i.e. for live trading it only keep tracks of the P/L for the day
            double cumulativePL = SystemPerformance.AllTrades.TradesPerformance.NetProfit; // cumulative P&L

            // MyPrint out the net profit of all trades
            MyPrint(defaultErrorType, "$$$$$$$$$$$$$$$$$$$$$$$$$$$$");
            MyPrint(defaultErrorType, "PrintProfitLossCurrentCapital, Cumulative net profit is: " + cumulativePL);
            MyPrint(defaultErrorType, "$$$$$$$$$$$$$$$$$$$$$$$$$$$$");

            // MyPrint out the current capital with P/L
            MyPrint(defaultErrorType, "$$$$$$$$$$$$$$$$$$$$$$$$$$$$");
            MyPrint(defaultErrorType, "PrintProfitLossCurrentCapital, Virtual current capital is: " + virtualCurrentCapital);
            MyPrint(defaultErrorType, "$$$$$$$$$$$$$$$$$$$$$$$$$$$$");

            // ouput current capital to cc file
            swCC = File.CreateText(pathCC); // Open the path for current capital
            swCC.WriteLine(virtualCurrentCapital); // overwrite current capital to cc file, if no existing file, InitStartingCapital will be written as currentCapital
            swCC.Close();
            swCC.Dispose();
            swCC = null;

            // ouput current monthly losses to cl file, currentMonthlyLosses is updated only ONCE during start up
            swCL = File.CreateText(pathCL); // Open the path for current monthly losses
            swCL.WriteLine(currentMonthlyLosses + cumulativePL); // overwrite current monthly losses to cl file
            swCL.Close();
            swCL.Dispose();
            swCL = null;
        }


        private void ComputeDynamicPstopsLstops(double highOfDay, double lowOfDay)
        {
            Int32 pStops, lStops;

            // 50% of the daily range between peak and trough, round to even number
            pStops = (int)Math.Round((highOfDay - lowOfDay) * 0.5, MidpointRounding.ToEven);

            // pStops can only be between 20 and 4
            if (pStops > 20)
                pStops = 20;
            if (pStops < 10)
                pStops = 10;

            lStops = pStops / 2;

            // save pStops in file 
            swPstops = File.CreateText(pathPstops); // Open the path for pStops
            swPstops.WriteLine(pStops.ToString());
            swPstops.Close();
            swPstops.Dispose();
            swPstops = null;
        }


        // Attempt to flatten position with limit order if EOD
        private void HandleEndOfSession()
        {
            DateTime endSessionTime;

            yesterdayVirtualCapital = virtualCurrentCapital;

            // pick the correct End session time
            if (Time[0].DayOfWeek == DayOfWeek.Friday)
            {
                endSessionTime = fridayEndSessionTime;
            }
            else
            {
                endSessionTime = regularEndSessionTime;
            }

            if (!endSession && Time[0].Hour == endSessionTime.Hour)
            {
                if (Time[0].Minute > endSessionTime.Minute)
                {
                    MyPrint(defaultErrorType, "HandleEndOfSession, Time= " + endSessionTime.ToString("HH:mm"));
                    MyPrint(defaultErrorType, "HandleEndOfSession, Current Time[0]= " + Time[0].ToString("HH:mm"));

                    CloseCurrentPositions();

                    // No need to reset server in Live trading
                    //ResetServer();
                    endSession = true;

                    SetDailyWinLossState();

                    MyPrint(defaultErrorType, "^^^^^^^^^^^^ High of the day=" + highOfDay);
                    MyPrint(defaultErrorType, "vvvvvvvvvvvv Low of the day=" + lowOfDay);
                    ComputeDynamicPstopsLstops(highOfDay, lowOfDay);
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
                // Skip all previous day bars until second bar of the day
                if (!Bars.GetTime(CurrentBar).Date.ToString("dd/MM/yyyy").Equals(DateTime.Now.ToString("dd/MM/yyyy")))
                    return;
                else
                {
                    // special handling of first bar to server
                    if (firstBarOfDay)
                    {
                        firstBarOfDay = false;
                        return;
                    }

                    // record high of day and low of day during trading hours, captured both Historical and Realtime data of the day
                    if (Bars.GetHigh(CurrentBar) > highOfDay)
                        highOfDay = Bars.GetHigh(CurrentBar);
                    if (Bars.GetLow(CurrentBar) < lowOfDay)
                        lowOfDay = Bars.GetLow(CurrentBar);

                    MyPrint(defaultErrorType, "^^^^^^^^^^^^^^ highOfDay=" + highOfDay + " lowOfDay=" + lowOfDay + " vvvvvvvvvvvvvvvvvvvvv");
                }

                // skip all historical bars until about 10 bars before real time bar, this is to prime the server before real time trading
                // BarsRequiredToTrade set to 240, is defined in the strategy property. MaximumBarsLookBack is set to 256.
                if (CurrentBar < BarsRequiredToTrade)
                {
                    // set lineNo to 0 for the "first bar" to server
                    lineNo = 0;
                    return;
                }

                // during live trading, flatten all virtual positions when loading historical data, real time trading will start with flat position
                // See StartBehavior = StartBehavior.WaitUntilFlatSynchronizeAccount; 
                if (State != State.Realtime && !PosFlat())
                {
                    FlattenVirtualPositions(); // this will flatten virtual positions and reset all flags
                }

                if (State == State.Realtime)
                {
                    // If failed to exit position with limit order, switch to exit with market order
                    if (attemptToFlattenPos && !PosFlat())
                    {
                        MyErrPrint(ErrorType.warning, "*******Failed to exit position using LIMIT ORDER, attemptToFlattenPos=" + attemptToFlattenPos + " Now exit position using MARKET ORDER.");
                        AiFlat(ExitOrderType.market);

                        // skip further processing until after position exit
                        return;
                    }

                    // Handle end of session ONLY when State == State.Realtime
                    if (!endSession)
                    {
                        // Attempt to flatten position with limit order if EOD
                        HandleEndOfSession();

                        // skip to next bar if EOD
                        if (endSession)
                            return;
                    }
                    else // EOD
                        return;  // skip all subsequence bars after EOD
                }

                string bufString;

                if (Bars.IsFirstBarOfSession)
                {
                    // construct the string buffer to be sent to DLNN
                    bufString = lineNo.ToString() + ',' +
                        "000000" + ',' + Bars.GetTime(CurrentBar).ToString("HHmmss") + ',' +
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
                }
                else
                {
                    // construct the string buffer to be sent to DLNN
                    bufString = lineNo.ToString() + ',' +
                        Bars.GetTime(CurrentBar - 1).ToString("HHmmss") + ',' + Bars.GetTime(CurrentBar).ToString("HHmmss") + ',' +
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
                }
                MyPrint(defaultErrorType, "CurrentBar = " + CurrentBar + ": " + "bufString = " + bufString);
                MyPrint(defaultErrorType, "SMA9=" + SMA(9)[0].ToString() + " SMA20=" + SMA(20)[0].ToString() + " RSI=" + RSI(14, 3)[0].ToString());
                MyPrint(defaultErrorType, "VROC=" + VROC(25, 3)[0].ToString() + " MACD=" + MACD(12, 26, 9).Diff[0].ToString());
                MyPrint(defaultErrorType, "Market Outlook = {{{{{ " + currMarketView.ToString() + " }}}}}");

                // Play a sound when current bar hit VROCUpper or VROCLower
                if ((VROC(25, 3)[0] >= VROCUpper) || (VROC(25, 3)[0] <= VROCLower))
                    PlaySound(@"C:\Program Files (x86)\NinjaTrader 8\sounds\chime_up.wav");

                byte[] msg = Encoding.UTF8.GetBytes(bufString);


                int bytesSent;
                int bytesRec;

                try
                {
                    // Send the data through the socket.  
                    bytesSent = sender.Send(msg);

                    // Receive the response from the remote device.  
                    bytesRec = sender.Receive(bytes);
                }
                catch (SocketException ex)
                {
                    MyErrPrint(ErrorType.fatal, "Socket exception::" + ex.Message + " " + ex.ToString());
                    if (!PosFlat())
                        MyErrPrint(ErrorType.fatal, "There may be an outstanding position for this strategy, manual flattening of the position may be needed.");
                }

                //svrSignal = ExtractResponse(System.Text.Encoding.UTF8.GetString(bytes, 0, bytes.Length));
                svrSignal = System.Text.Encoding.UTF8.GetString(bytes, 0, bytes.Length).Split(',')[1];
                MyPrint(defaultErrorType, "OnBarUpdate, Server response= <" + svrSignal + "> Current Bar: Open=" + Bars.GetOpen(CurrentBar) + " Close=" + Bars.GetClose(CurrentBar) + " High=" + Bars.GetHigh(CurrentBar) + " Low=" + Bars.GetLow(CurrentBar));
                //MyPrint(Bars.GetTime(CurrentBar).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + " Server response= <" + svrSignal + ">");

                lineNo++;

                // Start processing signal after 8th signal and beyond, otherwise ignore
                if (lineNo >= 8)
                {
                    ExecuteAITrade(svrSignal);

                    // if position is flat, no need to do anything
                    if (PosFlat())
                        return;

                    // handle stop loss or profit chasing if there is existing position and order action is either SellShort or Buy
                    if (entryOrder != null && (entryOrder.OrderAction == OrderAction.Buy || entryOrder.OrderAction == OrderAction.SellShort) && (entryOrder.OrderState == OrderState.Filled || entryOrder.OrderState == OrderState.PartFilled))
                    {
                        if (UseExitFilter)
                            HandleMarketShift();

                        // if Close[0] violates soft deck, if YES handle stop loss accordingly
                        if (ViolateSoftDeck())
                        {
                            HandleSoftDeck(svrSignal);
                        }

                        // if profitChasingFlag is TRUE or TouchedProfitChasing then handle profit chasing
                        if ((profitChasingFlag || TouchedProfitChasing()))
                        {
                            HandleProfitChasing(svrSignal);
                        }
                    }
                }
            }
            // When the OnBarUpdate() is called from the secondary bar series, in our case for each tick, handle End of session
            else if (BarsInProgress == 1)
            {
                if (State != State.Realtime)
                    return;

                // If in attemptToFlattenPos then don't do anything until after position flatten 
                if (attemptToFlattenPos && !PosFlat())
                    return;

                // HandleEndOfSession would close all positions
                if (!endSession && ViolateHardDeck())
                {
                    HandleHardDeck();
                }
                return;
            }
            // ^VIX daily data
            else if (BarsInProgress == 2)
            {
                MyPrint(defaultErrorType, "OnBarUpdate, ======================================================");
                MyPrint(defaultErrorType, "OnBarUpdate, ^VIX 10 days EMA " + EMA(BarsArray[2], 10)[0]);
                MyPrint(defaultErrorType, "OnBarUpdate, ======================================================");

                // write 10 days EMA VIX into VIX file 
                swVIX = File.CreateText(pathVIX); // Open the path for VIX
                swVIX.WriteLine(EMA(BarsArray[2], 10)[0].ToString());
                swVIX.Close();
                swVIX.Dispose();
                swVIX = null;

                MyPrint(defaultErrorType, "OnBarUpdate, ======================================================");
                MyPrint(defaultErrorType, "OnBarUpdate, ^VIX 10 days SMA " + SMA(BarsArray[2], 10)[0]);
                MyPrint(defaultErrorType, "OnBarUpdate, ======================================================");
            }
        }
    }
}
