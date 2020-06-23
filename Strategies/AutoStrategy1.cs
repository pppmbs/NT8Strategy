//
// Copyright (C) 2020, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
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
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// This namespace holds strategies in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Strategies
{
	public class AutoStrategy1 : Strategy
	{
		private Indicators.CandleStickPatternLogic candleStickPatternLogic;
		private DateTime                           endTimeForShortEntries;
		private Data.SessionIterator               sessionIterator;
		private DateTime                           startTimeForShortEntries;
		
		protected override void OnStateChange()
		{
			base.OnStateChange();

			if (State == State.SetDefaults)
			{
				IncludeTradeHistoryInBacktest             = false;
				IsInstantiatedOnEachOptimizationIteration = true;
				MaximumBarsLookBack                       = MaximumBarsLookBack.TwoHundredFiftySix;
				Name                                      = "AutoStrategy1";
				SupportsOptimizationGraph                 = false;
			}
			else if (State == State.Configure)
			{
				candleStickPatternLogic = new CandleStickPatternLogic(this, 2);
				SetParabolicStop(CalculationMode.Percent, 0.0175);
			}
			else if (State == State.DataLoaded)
			{
				AddChartIndicator(CandlestickPattern(ChartPattern.MorningStar, 2));
			}
		}

		protected override void OnBarUpdate()
		{
			base.OnBarUpdate();

			if (CurrentBars[0] < BarsRequiredToTrade)
				return;

			if (sessionIterator == null || BarsArray[0].IsFirstBarOfSession)
			{
				if (sessionIterator == null)
				{
					sessionIterator = new Data.SessionIterator(BarsArray[0]);
					sessionIterator.GetNextSession(Times[0][0], true);
				}
				else if (BarsArray[0].IsFirstBarOfSession)
					sessionIterator.GetNextSession(Times[0][0], true);

				startTimeForShortEntries  = sessionIterator.ActualSessionBegin.AddMinutes(15);
				endTimeForShortEntries    = startTimeForShortEntries.AddMinutes(45);
			}

			if (candleStickPatternLogic.Evaluate(ChartPattern.MorningStar)
				&& startTimeForShortEntries < Times[0][0] && Times[0][0] <= endTimeForShortEntries
				&& (Times[0][0].DayOfWeek == DayOfWeek.Monday || Times[0][0].DayOfWeek == DayOfWeek.Tuesday || Times[0][0].DayOfWeek == DayOfWeek.Thursday || Times[0][0].DayOfWeek == DayOfWeek.Saturday))
				EnterShort();
		}
	}
}
