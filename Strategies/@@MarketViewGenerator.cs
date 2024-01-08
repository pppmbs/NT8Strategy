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
		private string pathBolView;
		private StreamWriter swMkt = null; // Store marekt view, 0=Bear, 1=Neutral, 2=Bull
		private StreamWriter swBol = null; // Store bollinger view, 0=Sell, 1=Neutral, 2=Buy

		private string pathIdentViews;
		private StreamWriter swIDV = null; // 0==no identical market views, 1==consecutive identidcal market views

		private bool identicalViews = false;

		// Macro Market Views
		enum MarketView
		{
			Bullish,
			Bearish,
			Neutral,
			Undetermined
		};
		MarketView currMarketView = MarketView.Undetermined;

		enum BollingerView
		{
			Buy,
			Sell,
			Neutral
		};
		BollingerView bollingerView = BollingerView.Neutral;
		private static double BollingerWidth = 15;

		Queue<MarketView> mvQue = new Queue<MarketView>();
		private static int mvSize = 6;

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

				// Output tab2
				PrintTo = PrintTo.OutputTab2;
			}
		}

		private bool IdenticalMarketViews()
		{
			MarketView first;
			if (mvQue.Count != 0)
			{
				first = mvQue.Peek();
				Print(DateTime.Now + " IdenticalMarketViews, first=" + first.ToString());
			}
			else
			{
				Print(DateTime.Now + " IdenticalMarketViews, count=0");
				return false;
			}

			if (mvQue.Count < mvSize)
				return false;
			else
			{
				foreach (var mkt in mvQue)
				{
					Print(DateTime.Now + " IdenticalMarketViews, mkt=" + mkt.ToString());
					if (mkt != first)
						return false;
				}
			}
			return true;
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

		private void WriteBollingerView(BollingerView bolView)
		{
			pathBolView = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "runlog");
			pathBolView = System.IO.Path.Combine(pathBolView, "Artista" + ".bol");

			Print("WriteBollingerView: bollingerView =" + bollingerView.ToString());

			swBol = File.CreateText(pathBolView); // Open the path for Bollinger View
			switch (bolView)
			{
				case BollingerView.Buy:
					swBol.WriteLine("2");
					break;
				case BollingerView.Sell:
					swBol.WriteLine("0");
					break;
				default:
					swBol.WriteLine("1");
					break;
			}
			swBol.Close();
			swBol.Dispose();
			swBol = null;
		}

		private void WriteIdenticalViews()
        {
			pathIdentViews = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "runlog");
			pathIdentViews = System.IO.Path.Combine(pathIdentViews, "Artista" + ".idv");

			swIDV = File.CreateText(pathIdentViews); // Open the path for Identical Market Views

			if (IdenticalMarketViews())
            {
				swIDV.WriteLine("1");
				Print(DateTime.Now + " WriteIdenticalViews: <<<< Identical Market Views: TRUE >>>>");
			}
			else
            {
				swIDV.WriteLine("0");
				Print(DateTime.Now + " WriteIdenticalViews: <<<< Identical Market Views: FALSE >>>>");
			}

			swIDV.Close();
			swIDV.Dispose();
			swIDV = null;
		}


		private void SetBollingerView()
		{
			double midBollinger = (Bollinger(2, 20).Lower[0] + Bollinger(2, 20).Upper[0]) / 2;

			// skip first bar of session
			if (Bars.IsFirstBarOfSession)
				return;

			Print("midBollinger=" + midBollinger.ToString() + " Bollinger(2, 20).Upper[0]=" + Bollinger(2, 20).Upper[0].ToString() + " Bollinger(2, 20).Lower[0]=" + Bollinger(2, 20).Lower[0].ToString());

			// if Bollinger band is narrow, i.e. < BollingerWidth in width, then bollinger view is neutral
			if ((Bollinger(2, 20).Upper[0] - Bollinger(2, 20).Lower[0]) < BollingerWidth)
			{
				bollingerView = BollingerView.Neutral;
				WriteBollingerView(bollingerView);
				return;
			}
			// if current close price is higher or lower than bollinger band, then bollinger view is neutral 
			if (Close[0] >= Bollinger(2, 20).Upper[0] || Close[0] <= Bollinger(2, 20).Lower[0])
			{
				bollingerView = BollingerView.Neutral;
				WriteBollingerView(bollingerView);
				return;
			}

			// capture the moment when price crosses the bollinger mid point
			if (Close[0] < midBollinger && Close[0] < Close[1])
            {
				bollingerView = BollingerView.Sell;
				PlaySound(@"C:\Program Files (x86)\NinjaTrader 8\sounds\chime_down.wav");
			}
			else if (Close[0] > midBollinger && Close[0] > Close[1])
			{
				bollingerView = BollingerView.Buy;
				PlaySound(@"C:\Program Files (x86)\NinjaTrader 8\sounds\chime_up.wav");
			}
			else
				bollingerView = BollingerView.Neutral;

			WriteBollingerView(bollingerView);
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress == 0)
			{
			}
			else if (BarsInProgress == 1) // 5 mins
			{
				// Use Bollinger Band on 5 mins chart to establish bollinger market views
				SetBollingerView();

				// Use RSI on 5 mins chart to establish market view
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

				// Insert current market view in queue
				if (mvQue.Count == mvSize)
				{
					mvQue.Dequeue();
					mvQue.Enqueue(currMarketView);
				}
				else
					mvQue.Enqueue(currMarketView);

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

				Print(DateTime.Now + " Bollinger View = [[[[[ " + bollingerView.ToString() + " ]]]]]");

				WriteMarketView(currMarketView);
				WriteIdenticalViews();
				Print(DateTime.Now + " Current Market View = {{{{{ " + currMarketView.ToString() + " }}}}} ");
			}
		}
	}
}
