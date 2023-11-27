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
	public class BollingerViewFile : Strategy
	{
		string path;

		private static double RSIHigh = 80;
		private static double RSILow = 20;
		private static double BullConfirmation = 60;
		private static double BearConfirmation = 40;

		// Macro Market Views
		enum BollingerView
		{
			Buy,
			Sell,
			Neutral
		};
		BollingerView bollingerView = BollingerView.Neutral;
		private static double BollingerWidth = 15;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = @"Generate daily bollinger market view files";
				Name = "BollingerViewFile";
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

		private void SetBollingerView()
		{
			double midBollinger = (Bollinger(2, 20).Lower[0] + Bollinger(2, 20).Upper[0]) / 2;

			// skip first bar of session
			if (Bars.IsFirstBarOfSession)
				return;

			// if Bollinger band is narrow, i.e. < BollingerWidth in width, then bollinger view is neutral
			if ((Bollinger(2, 20).Upper[0] - Bollinger(2, 20).Lower[0]) < BollingerWidth)
			{
				bollingerView = BollingerView.Neutral;
				return;
			}
			// if current close price is higher or lower than bollinger band, then bollinger view is neutral 
			if (Close[0] >= Bollinger(2, 20).Upper[0] || Close[0] <= Bollinger(2, 20).Lower[0])
			{
				bollingerView = BollingerView.Neutral;
				return;
			}

			// capture the moment when price crosses the bollinger mid point
			if (Close[0] < midBollinger && Close[0] < Close[1])
				bollingerView = BollingerView.Sell;
			else if (Close[0] > midBollinger && Close[0] > Close[1])
				bollingerView = BollingerView.Buy;
			else
				bollingerView = BollingerView.Neutral;
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

				string bufString;
				string header = "TIME,VIEW";

				if (Bars.IsFirstBarOfSession)
				{
					path = NinjaTrader.Core.Globals.UserDataDir + "BollingerViewFile\\" + Bars.GetTime(CurrentBar).ToString("yyyyMMdd") + ".csv";

					// construct the string buffer to be sent to DLNN
					bufString = "000000" + ',' + bollingerView.ToString();

					File.Delete(path);
					File.AppendAllText(path, header + Environment.NewLine);
					Print(header);
					Print(Bars.GetTime(CurrentBar).ToString("d"));
				}
				else
				{
					// construct the string buffer to be sent to DLNN
					bufString = Bars.GetTime(CurrentBar).ToString("HHmmss") + ',' + bollingerView.ToString();
				}
				File.AppendAllText(path, bufString + Environment.NewLine);
				Print(bufString);
			}
		}
	}
}
