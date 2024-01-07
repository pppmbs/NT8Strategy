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
using System.IO;
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
	public class MarketViewFile : Strategy
	{
		string path;

		private static double RSIHigh = 80;
		private static double RSILow = 20;
		private static double BullConfirmation = 60;
		private static double BearConfirmation = 40;

		private string pathMktView;
		private StreamWriter swMkt = null; // Store marekt view, 0=Bear, 1=Neutral, 2=Bull

		// Macro Market Views
		enum MarketView
		{
			Bullish,
			Bearish,
			Neutral,
			Undetermined
		};
		MarketView currMarketView = MarketView.Undetermined;

		enum RSIRegion
		{
			Ready,
			BullConfirmed,
			BearConfirmed,
			Start
		};
		RSIRegion rsiTouched = RSIRegion.Start;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = @"Generate daily market view files";
				Name = "MarketViewFile";
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
				AddDataSeries(Data.BarsPeriodType.Minute, 5);

				// Output tab1
				PrintTo = PrintTo.OutputTab1;
			}
		}


		protected override void OnBarUpdate()
		{
			if (BarsInProgress == 0)
			{
			}
			else if (BarsInProgress == 1) // 5 mins
			{
				// Use RSI on 5 mins chart to establish market views

				// Neutral
				if (RSI(14, 3)[0] >= RSIHigh)
				{
					rsiTouched = RSIRegion.Ready;
					currMarketView = MarketView.Neutral;
				}

				// Neutral
				if (RSI(14, 3)[0] <= RSILow)
				{
					rsiTouched = RSIRegion.Ready;
					currMarketView = MarketView.Neutral;
				}

				// Neutral -> Bullish
				if ((RSI(14, 3)[0] >= BullConfirmation) && ((rsiTouched == RSIRegion.Ready) || (currMarketView == MarketView.Neutral)))
				{
					rsiTouched = RSIRegion.BullConfirmed;
					currMarketView = MarketView.Bullish;
				}

				// Neutral -> Bearish
				if ((RSI(14, 3)[0] <= BearConfirmation) && ((rsiTouched == RSIRegion.Ready) || (currMarketView == MarketView.Neutral)))
				{
					rsiTouched = RSIRegion.BearConfirmed;
					currMarketView = MarketView.Bearish;
				}

				// Bullish -> Neutral, RSI turned back below BullConfirmation
				if ((RSI(14, 3)[0] < BullConfirmation) && (rsiTouched == RSIRegion.BullConfirmed))
				{
					currMarketView = MarketView.Neutral;
				}

				// Bearish -> Neutral, RSI turned back above BearConfirmation
				if ((RSI(14, 3)[0] > BearConfirmation) && (rsiTouched == RSIRegion.BearConfirmed))
				{
					currMarketView = MarketView.Neutral;
				}

				string bufString;
				string header = "TIME,VIEW";

				if (Bars.IsFirstBarOfSession)
				{
					path = NinjaTrader.Core.Globals.UserDataDir + "MarketViewFile\\" + Bars.GetTime(CurrentBar).ToString("yyyyMMdd") + ".csv";

					// construct the string buffer to be sent to DLNN
					bufString = "000000" + ',' + currMarketView.ToString();

					File.Delete(path);
					File.AppendAllText(path, header + Environment.NewLine);
					Print(header);
					Print(Bars.GetTime(CurrentBar).ToString("d"));
				}
				else
				{
					// construct the string buffer to be sent to DLNN
					bufString = Bars.GetTime(CurrentBar).ToString("HHmmss") + ',' + currMarketView.ToString();
				}
				File.AppendAllText(path, bufString + Environment.NewLine);
				Print(bufString);
			}
		}
	}
}
