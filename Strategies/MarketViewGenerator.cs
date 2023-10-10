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
	public class MarketViewGenerator : Strategy
	{
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
				Description = @"Experimental with indicators  to establish market view";
				Name = "MarketViewGenerator";
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
			}
		}

		private void WriteMarketView(MarketView mktView)
        {
			pathMktView = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "runlog");
			pathMktView = System.IO.Path.Combine(pathMktView, "Artista" + ".mkt");

			swMkt = File.CreateText(pathMktView); // Open the path for Market View
            switch (mktView)
            {
				case MarketView.Bullish:
					swMkt.WriteLine("2");
					break;
				case MarketView.Bearish:
					swMkt.WriteLine("0");
					break;
				default:
					swMkt.WriteLine("1");
					break;
			}
			swMkt.Close();
			swMkt.Dispose();
			swMkt = null;
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

				switch (currMarketView)
                {
					case MarketView.Neutral:
						PlaySound(@"C:\Program Files (x86)\NinjaTrader 8\sounds\ding.wav");
						break;
					case MarketView.Bearish:
						PlaySound(@"C:\Program Files (x86)\NinjaTrader 8\sounds\glass_shatter_c.wav");
						break;
					case MarketView.Bullish:
						PlaySound(@"C:\Program Files (x86)\NinjaTrader 8\sounds\bicycle_bell.wav");
						break;
                }

				WriteMarketView(currMarketView);
				Print("Current Market View =" + " {{{{{ " + currMarketView.ToString() + " }}}}} ");
				Print("Current Market View =" + " {{{{{ " + currMarketView.ToString() + " }}}}} ");
				Print("Current Market View =" + " {{{{{ " + currMarketView.ToString() + " }}}}} ");
			}
		}
	}
}
