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

using System.IO;

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
    public class Sample : Strategy
    {
        static bool ready=false;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Capturing the primary and secondary data from NT";
                Name = "Sample";
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
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 20;
                // Disable this property for performance gains in Strategy Analyzer optimizations
                // See the Help Guide for additional information
                IsInstantiatedOnEachOptimizationIteration = true;
            }
            else if (State == State.Configure)
            {
                /* Add a secondary bar series.*/
                AddDataSeries(Data.BarsPeriodType.Tick, 200);
                AddDataSeries(Data.BarsPeriodType.Tick, 400);
            }
        }

        protected override void OnBarUpdate()
        {
            string bufString;

            if (BarsInProgress == 0)
            {
                // construct the string buffer 
 if (Bars.IsFirstBarOfSession)
                {
                    // construct the string buffer to be sent to DLNN
                    bufString = 
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
                    bufString = 
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

                Print(bufString);
                ready = true;
            }
            /*
            else if (BarsInProgress == 1)
            {
                if (ready)
                {
                    // construct the string buffer 
                    bufString = "200 ticks:: " + Bars.GetTime(CurrentBars[0]).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + ',' +
                        Bars.GetOpen(CurrentBars[0]).ToString() + ',' + Bars.GetClose(CurrentBars[0]).ToString() + ',' +
                        Bars.GetHigh(CurrentBars[0]).ToString() + ',' + Bars.GetLow(CurrentBars[0]).ToString() + ',' +
                        Bars.GetVolume(CurrentBars[0]).ToString();

                    Print(bufString);
                }
            }
            else if (BarsInProgress == 2)
            {
                if (ready)
                {
                    // construct the string buffer 
                    bufString = "400 ticks:: " + Bars.GetTime(CurrentBars[0]).ToString("yyyy-MM-ddTHH:mm:ss.ffffffK") + ',' +
                        Bars.GetOpen(CurrentBars[0]).ToString() + ',' + Bars.GetClose(CurrentBars[0]).ToString() + ',' +
                        Bars.GetHigh(CurrentBars[0]).ToString() + ',' + Bars.GetLow(CurrentBars[0]).ToString() + ',' +
                        Bars.GetVolume(CurrentBars[0]).ToString();

                    Print(bufString);
                }
            }
            */
        }
    }
}
