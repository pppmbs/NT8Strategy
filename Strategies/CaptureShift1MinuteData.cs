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

/* ******* READ ME *******
 * 1, Run CaptureShift1MinuteData strategy in *** 1 minute *** time scale in Strategy Analyzer
 * 2, CaptureShift1MinuteData will generate shifted 1 minute data in ES 06-YY.Last.txt file format
 * 3, Upload ES 06-YY.Last.txt to NinjaTrader ES 06 historical database - remember to set appropriate upload time zone 
 * 4, It is UTC-6 for CST (Spring Forward), and UTC-5 for CDT - Central Daylight Time or Daylight Saving Time (Fall Back)
 * 5, Run Capture5MinuteData to get shifted primary and secondary data
 */
//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
    public class CaptureShift1MinuteData : Strategy
    {
        string path;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Capturing shifted 1 minute data in the format of NinjaTrader Historical ES file - RUN THIS STRATEGY IN 1 MINUTE TIME SCALE";
                Name = "CaptureShift1MinuteData";
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
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == 0)
            {
                string bufString;
                string header = "START_TIME,END_TIME,OPEN_PRICE,CLOSE_PRICE,HIGH_PRICE,LOW_PRICE,TOTAL_VOLUME,SMA9,SMA20,SMA50,MACD_DIFF,RSI,BOLL_LOW,BOLL_HIGH,CCI,ATR_TrueHigh,ATR_TrueLow,Momentum,ADX_DIPositive,ADX_DINegative,VROC,NEXT_OPEN_BAR1,NEXT_CLOSE_BAR1,NEXT_OPEN_BAR2,NEXT_CLOSE_BAR2,NEXT_OPEN_BAR3,NEXT_CLOSE_BAR3,NEXT_OPEN_BAR4,NEXT_CLOSE_BAR4,NEXT_OPEN_BAR5,NEXT_CLOSE_BAR5";
                path = NinjaTrader.Core.Globals.UserDataDir + "CaptureShift1MinuteData\\" + "ES 06-" + Bars.GetTime(CurrentBar).ToString("yy") + ".Last.txt";

                if (Bars.IsFirstBarOfSession)
                {
                    //skip first minute
                    return;
                }
                else
                {
                    // shifted 1 minute forward in time
                    bufString = Bars.GetTime(CurrentBar).ToString("yyyyMMdd") + " " +
                        Bars.GetTime(CurrentBar - 1).ToString("HHmmss") + ';' +
                        Bars.GetOpen(CurrentBar).ToString() + ';' +
                        Bars.GetHigh(CurrentBar).ToString() + ';' +
                        Bars.GetLow(CurrentBar).ToString() + ';' +
                        Bars.GetClose(CurrentBar).ToString() + ';' +
                        Bars.GetVolume(CurrentBar).ToString();
                    File.AppendAllText(path, bufString + Environment.NewLine);
                    Print(bufString);
                }
            }
        }
    }
}
