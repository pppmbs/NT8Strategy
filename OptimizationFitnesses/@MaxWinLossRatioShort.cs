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
using System.Windows.Media;
using System.Xml.Serialization;
#endregion

namespace NinjaTrader.NinjaScript.OptimizationFitnesses
{
	public class MaxWinLossRatioShort : OptimizationFitness
	{
		protected override void OnCalculatePerformanceValue(StrategyBase strategy)
		{
			if (strategy.SystemPerformance.ShortTrades.LosingTrades.TradesPerformance.Percent.AverageProfit == 0)
				Value = 1;
			else
				Value = strategy.SystemPerformance.ShortTrades.WinningTrades.TradesPerformance.Percent.AverageProfit / Math.Abs(strategy.SystemPerformance.ShortTrades.LosingTrades.TradesPerformance.Percent.AverageProfit);
		}

		protected override void OnStateChange()
		{               
			if (State == State.SetDefaults)
				Name = NinjaTrader.Custom.Resource.NinjaScriptOptimizationFitnessNameMaxWinLossRatioShort;
		}
	}
}
