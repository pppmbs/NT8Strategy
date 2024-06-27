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
 * 1, Run CaptureShift1MinuteData strategy in *** 1 minute *** bar in Strategy Analyzer, Type=Minute, Value=1
 * 2, CaptureShift1MinuteData will generate shifted 1 through 4 minutes data in ES 06-YY.Last.txt file format in 1-4 folders
 * 3, REMOVE existing ES 06-YY before Uploading ES 06-YY.Last.txt to NinjaTrader ES 06 historical database - remember to set appropriate upload time zone
 * 4, It is UTC-6 for CST (Spring Forward), and UTC-5 for CDT - Central Daylight Time or Daylight Saving Time (Fall Back)
 * 5, From Instrument ES 06-YY, run Capture5MinuteData (4 times, one for each shifted ES 06-YY) to get shifted primary and secondary data, run in 5 min bar, Type=Minute, Value=5
 */
//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{

    public class CaptureShift1MinuteData : Strategy
    {
        string path1, path2, path3, path4;
        int skip1 = 1; // from 1 to 4
        int skip2 = 2;
        int skip3 = 3;
        int skip4 = 4;
        int skipCount;

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
                string bufString = "";

                path1 = NinjaTrader.Core.Globals.UserDataDir + "CaptureShift1MinuteData\\1\\" + "ES 06-" + Bars.GetTime(CurrentBar).ToString("yy") + ".Last.txt.";
                path2 = NinjaTrader.Core.Globals.UserDataDir + "CaptureShift1MinuteData\\2\\" + "ES 06-" + Bars.GetTime(CurrentBar).ToString("yy") + ".Last.txt.";
                path3 = NinjaTrader.Core.Globals.UserDataDir + "CaptureShift1MinuteData\\3\\" + "ES 06-" + Bars.GetTime(CurrentBar).ToString("yy") + ".Last.txt.";
                path4 = NinjaTrader.Core.Globals.UserDataDir + "CaptureShift1MinuteData\\4\\" + "ES 06-" + Bars.GetTime(CurrentBar).ToString("yy") + ".Last.txt.";

                if (Bars.IsFirstBarOfSession)
                {
                    //reset skipCount daily
                    skipCount = 1;
                    return;
                }
                else
                {
                    // skip 1st minute
                    if (skipCount >= skip1)
                    {
                        // shifted 1 minute forward in time
                        bufString = Bars.GetTime(CurrentBar).ToString("yyyyMMdd") + " " +
                            Bars.GetTime(CurrentBar - skip1).ToString("HHmmss") + ';' +
                            Bars.GetOpen(CurrentBar).ToString() + ';' +
                            Bars.GetHigh(CurrentBar).ToString() + ';' +
                            Bars.GetLow(CurrentBar).ToString() + ';' +
                            Bars.GetClose(CurrentBar).ToString() + ';' +
                            Bars.GetVolume(CurrentBar).ToString();
                        File.AppendAllText(path1, bufString + Environment.NewLine);
                    }
                    // skip 2nd minute
                    if (skipCount >= skip2)
                    {
                        // shifted 2 minutes forward in time
                        bufString = Bars.GetTime(CurrentBar).ToString("yyyyMMdd") + " " +
                            Bars.GetTime(CurrentBar - skip2).ToString("HHmmss") + ';' +
                            Bars.GetOpen(CurrentBar).ToString() + ';' +
                            Bars.GetHigh(CurrentBar).ToString() + ';' +
                            Bars.GetLow(CurrentBar).ToString() + ';' +
                            Bars.GetClose(CurrentBar).ToString() + ';' +
                            Bars.GetVolume(CurrentBar).ToString();
                        File.AppendAllText(path2, bufString + Environment.NewLine);
                    }
                    // skip 3rd minute
                    if (skipCount >= skip3)
                    {
                        // shifted 3 minutes forward in time
                        bufString = Bars.GetTime(CurrentBar).ToString("yyyyMMdd") + " " +
                            Bars.GetTime(CurrentBar - skip3).ToString("HHmmss") + ';' +
                            Bars.GetOpen(CurrentBar).ToString() + ';' +
                            Bars.GetHigh(CurrentBar).ToString() + ';' +
                            Bars.GetLow(CurrentBar).ToString() + ';' +
                            Bars.GetClose(CurrentBar).ToString() + ';' +
                            Bars.GetVolume(CurrentBar).ToString();
                        File.AppendAllText(path3, bufString + Environment.NewLine);
                    }
                    // skip 4th minute
                    if (skipCount >= skip4)
                    {
                        // shifted 3 minutes forward in time
                        bufString = Bars.GetTime(CurrentBar).ToString("yyyyMMdd") + " " +
                            Bars.GetTime(CurrentBar - skip3).ToString("HHmmss") + ';' +
                            Bars.GetOpen(CurrentBar).ToString() + ';' +
                            Bars.GetHigh(CurrentBar).ToString() + ';' +
                            Bars.GetLow(CurrentBar).ToString() + ';' +
                            Bars.GetClose(CurrentBar).ToString() + ';' +
                            Bars.GetVolume(CurrentBar).ToString();
                        File.AppendAllText(path4, bufString + Environment.NewLine);
                    }

                    skipCount++;
                    Print(bufString);
                }
            }
        }
    }
}
