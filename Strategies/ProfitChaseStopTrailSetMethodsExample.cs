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

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
	public class ProfitChaseStopTrailSetMethodsExample : Strategy
	{
		private double currentPtPrice, currentSlPrice;
		private bool exitOnCloseWait;
		private SessionIterator sessionIterator;

		private int tradeCount = 0;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = @"Managed Set Order Example";
				Name = "ProfitChaseStopTrailSetMethodsExample";
				Calculate = Calculate.OnBarClose;
				EntriesPerDirection = 1;
				EntryHandling = EntryHandling.AllEntries;
				IsExitOnSessionCloseStrategy = true;
				ExitOnSessionCloseSeconds = 30;
				IsFillLimitOnTouch = false;
				TraceOrders = false;
				BarsRequiredToTrade = 1;
				IsInstantiatedOnEachOptimizationIteration = false;

				ChaseProfitTarget = true;
				PrintDetails = false;
				ProfitTargetDistance = 10;
				StopLossDistance = 10;
				TrailStopLoss = true;
				UseProfitTarget = true;
				UseStopLoss = true;
			}
			else if (State == State.Configure)
			{
				AddDataSeries(BarsPeriodType.Tick, 1);
			}
			else if (State == State.DataLoaded)
			{
				sessionIterator = new SessionIterator(Bars);
				exitOnCloseWait = false;
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBars[0] < BarsRequiredToTrade || CurrentBars[1] < BarsRequiredToTrade)
				return;

			if (BarsInProgress == 0)
			{
				if (CurrentBar == 0 || Bars.IsFirstBarOfSession)
					sessionIterator.GetNextSession(Time[0], true);

				// if after the exit on close time, prevent new orders until the new session
				if (Times[1][0] >= sessionIterator.ActualSessionEnd.AddSeconds(-ExitOnSessionCloseSeconds) && Times[1][0] <= sessionIterator.ActualSessionEnd)
					exitOnCloseWait = true;

				// reset for a new entry on the first bar of a new session
				else if (exitOnCloseWait && Bars.IsFirstBarOfSession)
					exitOnCloseWait = false;

				if (State == State.Historical && CurrentBar == BarsArray[0].Count - 2 && Position.MarketPosition == MarketPosition.Long)
				{
					ExitLong(1, 1, "exit to start flat", string.Empty);
				}

				else if (!exitOnCloseWait && Position.MarketPosition == MarketPosition.Flat)
				{
					// Reset the stop loss to the original distance when all positions are closed before placing a new entry

					currentPtPrice = Close[0] + ProfitTargetDistance * TickSize;
					currentSlPrice = Close[0] - StopLossDistance * TickSize;

					if (UseProfitTarget)
						SetProfitTarget(CalculationMode.Price, currentPtPrice);

					if (UseStopLoss)
						SetStopLoss(CalculationMode.Price, currentSlPrice);

					Print(string.Format("ProfitChaseStopTrailSetMethodsExample:: tradeCount {0}", tradeCount++));

					EnterLong(1, 1, string.Empty);
				}
			}

			if (BarsInProgress == 1 && Position.MarketPosition == MarketPosition.Long)
			{
				if (UseProfitTarget && ChaseProfitTarget && Close[0] < currentPtPrice - ProfitTargetDistance * TickSize)
				{
					currentPtPrice = Close[0] + ProfitTargetDistance * TickSize;
					SetProfitTarget(CalculationMode.Price, currentPtPrice);
				}

				if (UseStopLoss && TrailStopLoss && Close[0] > currentSlPrice + StopLossDistance * TickSize)
				{
					currentSlPrice = Close[0] - StopLossDistance * TickSize;
					SetStopLoss(CalculationMode.Price, currentSlPrice);
				}
			}
		}

		#region Properties
		[NinjaScriptProperty]
		[Display(Name = "Chase profit target", Order = 2, GroupName = "NinjaScriptStrategyParameters")]
		public bool ChaseProfitTarget
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Profit target distance", Description = "Distance for profit target (in ticks)", Order = 3, GroupName = "NinjaScriptStrategyParameters")]
		public int ProfitTargetDistance
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Print details", Order = 7, GroupName = "NinjaScriptStrategyParameters")]
		public bool PrintDetails
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Stop loss distance", Description = "Distance for stop loss (in ticks)", Order = 6, GroupName = "NinjaScriptStrategyParameters")]
		public int StopLossDistance
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Trail stop loss", Order = 5, GroupName = "NinjaScriptStrategyParameters")]
		public bool TrailStopLoss
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use profit target", Order = 1, GroupName = "NinjaScriptStrategyParameters")]
		public bool UseProfitTarget
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Use stop loss", Order = 4, GroupName = "NinjaScriptStrategyParameters")]
		public bool UseStopLoss
		{ get; set; }
		#endregion
	}
}
