// 
// Copyright (C) 2022, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using System.Xml.Serialization;

using NinjaTrader.Core.FloatingPoint;
#endregion

namespace NinjaTrader.NinjaScript.Optimizers
{
	public class StrategyGenerator : Optimizer
	{
		private int						oldKeepBestResults			= -1;
		private	ChartPattern[]			selectedCandleStickPattern;
		private Type[]					selectedIndicatorTypes;

		public	static ChartPattern[]	AvailableCandleStickPattern	= Enum.GetValues(typeof(ChartPattern)).Cast<ChartPattern>().ToArray();

		// Subjective selection of indicators which 'make most sense'. The full set of indicators would be used if this selection would not be set
		// The optional tuple defines the range of random comparison values for oscillators (where such range could be defined)
		// Feel free to modify to your liking...
		internal	static Dictionary<Type, Tuple<double, double>> AvailableIndicators = new Dictionary<Type, Tuple<double, double>>()
														{
															{ typeof(Indicators.ADL),                       null },
															{ typeof(Indicators.ADX),                       new Tuple<double, double>(0, 100) },
															{ typeof(Indicators.ADXR),                      new Tuple<double, double>(0, 100) },
															{ typeof(Indicators.APZ),                       null },
															{ typeof(Indicators.Aroon),                     new Tuple<double, double>(0, 100) },
															{ typeof(Indicators.AroonOscillator),           new Tuple<double, double>(-100, 100) },
															{ typeof(Indicators.ATR),                       null },
															{ typeof(Indicators.Bollinger),                 null },
															{ typeof(Indicators.BOP),                       null },
															{ typeof(Indicators.CCI),                       null },
															{ typeof(Indicators.ChaikinMoneyFlow),			new Tuple<double, double>(-1, 1) },
															{ typeof(Indicators.ChaikinOscillator),         null },
															{ typeof(Indicators.ChaikinVolatility),         null },
															{ typeof(Indicators.CMO),                       new Tuple<double, double>(-100, 100) },
															{ typeof(Indicators.DM),                        new Tuple<double, double>(-100, 100) },
															{ typeof(Indicators.DMI),                       new Tuple<double, double>(-100, 100) },
															{ typeof(Indicators.EMA),                       null },
															{ typeof(Indicators.FisherTransform),           null },
															{ typeof(Indicators.FOSC),                      null },
															{ typeof(Indicators.HMA),                       null },
															{ typeof(Indicators.KAMA),                      null },
															{ typeof(Indicators.KeltnerChannel),            null },
															{ typeof(Indicators.KeyReversalDown),           new Tuple<double, double>(0, 1) },
															{ typeof(Indicators.KeyReversalUp),             new Tuple<double, double>(0, 1) },
															{ typeof(Indicators.LinReg),                    null },
															{ typeof(Indicators.LinRegIntercept),           null },
															{ typeof(Indicators.LinRegSlope),               null },
															{ typeof(Indicators.MACD),                      null },
															{ typeof(Indicators.MAEnvelopes),               null },
															{ typeof(Indicators.MAMA),                      null },
															{ typeof(Indicators.MFI),                       new Tuple<double, double>(0, 100) },
															{ typeof(Indicators.Momentum),                  null },
															{ typeof(Indicators.MoneyFlowOscillator),       new Tuple<double, double>(-1, 1) },
															{ typeof(Indicators.MovingAverageRibbon),       null },
															{ typeof(Indicators.NBarsDown),                 new Tuple<double, double>(0, 1) },
															{ typeof(Indicators.NBarsUp),                   new Tuple<double, double>(0, 1) },
															{ typeof(Indicators.OBV),                       null },
															{ typeof(Indicators.ParabolicSAR),              null },
															{ typeof(Indicators.PFE),                       new Tuple<double, double>(-100, 100) },
															{ typeof(Indicators.Pivots),                    null },
															{ typeof(Indicators.PPO),                       null },
															{ typeof(Indicators.PriceOscillator),           null },
															{ typeof(Indicators.Range),                     null },
															{ typeof(Indicators.RelativeVigorIndex),        null },
															{ typeof(Indicators.RIND),                      null },
															{ typeof(Indicators.ROC),                       null },
															{ typeof(Indicators.RSI),                       new Tuple<double, double>(0, 100) },
															{ typeof(Indicators.RSquared),                  new Tuple<double, double>(0, 1) },
															{ typeof(Indicators.RSS),                       new Tuple<double, double>(0, 100) },
															{ typeof(Indicators.RVI),                       new Tuple<double, double>(0, 100) },
															{ typeof(Indicators.SMA),                       null },
															{ typeof(Indicators.StdDev),                    null },
															{ typeof(Indicators.StdError),                  null },
															{ typeof(Indicators.Stochastics),               new Tuple<double, double>(0, 100) },
															{ typeof(Indicators.StochasticsFast),           new Tuple<double, double>(0, 100) },
															{ typeof(Indicators.StochRSI),                  new Tuple<double, double>(0, 100) },
															{ typeof(Indicators.Swing),                     null },
															{ typeof(Indicators.T3),                        null },
															{ typeof(Indicators.TEMA),                      null },
															{ typeof(Indicators.TMA),                       null },
															{ typeof(Indicators.TRIX),                      null },
															{ typeof(Indicators.TSF),                       null },
															{ typeof(Indicators.TSI),                       new Tuple<double, double>(-100, 100) },
															{ typeof(Indicators.UltimateOscillator),        new Tuple<double, double>(0, 100) },
															{ typeof(Indicators.VMA),                       null },
															{ typeof(Indicators.VOL),                       null },
															{ typeof(Indicators.VOLMA),                     null },
															{ typeof(Indicators.VolumeOscillator),          null },
															{ typeof(Indicators.VROC),                      null },
															{ typeof(Indicators.VWMA),                      null },
															{ typeof(Indicators.WilliamsR),                 new Tuple<double, double>(-100, 0) },
															{ typeof(Indicators.WMA),                       null },
															{ typeof(Indicators.ZLEMA),                     null },
														};

		public override void CopyTo(NinjaScript ninjaScript)
		{
			base.CopyTo(ninjaScript);

			if (ninjaScript is StrategyGenerator)
				(ninjaScript as StrategyGenerator).oldKeepBestResults = oldKeepBestResults;
		}

		public override bool IsStrategyGenerator
		{
			get {  return true; }
		}

		private List<Cbi.SystemPerformance> GetUniqueResults()
		{
			// remove long/short conditions in case they did not contribute to the trades
			// however, make sure there is at least one long trade or short trade (for consistency reasons)
			foreach (Cbi.SystemPerformance result in Results)
			{
				if (result.ParameterValues == null)
					continue;

				if (result.LongTrades.TradesCount == 0 && result.ShortTrades.TradesCount != 0)
				{
					(result.ParameterValues[0] as NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic).EnterLongCondition					= null;
					(result.ParameterValues[0] as NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic).ExitLongCondition					= null;
					(result.ParameterValues[0] as NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic).SessionMinutesForLongEntries		= -1;
					(result.ParameterValues[0] as NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic).SessionMinutesForLongExits			= -1;
					(result.ParameterValues[0] as NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic).SessionMinutesOffsetForLongEntries	= -1;
					(result.ParameterValues[0] as NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic).SessionMinutesOffsetForLongExits	= -1;
				}

				if (result.ShortTrades.TradesCount == 0 && result.LongTrades.TradesCount != 0)
				{
					(result.ParameterValues[0] as NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic).EnterShortCondition					= null;
					(result.ParameterValues[0] as NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic).ExitShortCondition					= null;
					(result.ParameterValues[0] as NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic).SessionMinutesForShortEntries		= -1;
					(result.ParameterValues[0] as NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic).SessionMinutesForShortExits			= -1;
					(result.ParameterValues[0] as NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic).SessionMinutesOffsetForShortEntries	= -1;
					(result.ParameterValues[0] as NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic).SessionMinutesOffsetForShortExits	= -1;
				}
			}

			// filter duplicate results and keep the ones with the least # of nodes
			// note: filtering is not 100% inline with the logic to mutate/crossover/... the next generation, since it could remove redundant individuals
			// however, it does not matter at the long run, as long as on starting the new generation there still are .GenerationSize individuals in the population
			List<Cbi.SystemPerformance> uniqueResults = new List<Cbi.SystemPerformance>();
			foreach (Cbi.SystemPerformance result in Results)
			{
				List<Cbi.SystemPerformance> results = Results.Where(r => result.ParameterValues != null
															&& result.PerformanceValue.ApproxCompare(r.PerformanceValue) == 0
															&& result.AllTrades.TradesPerformance.Percent.CumProfit.ApproxCompare(r.AllTrades.TradesPerformance.Percent.CumProfit) == 0
															&& result.AllTrades.LosingTrades.TradesCount == r.AllTrades.LosingTrades.TradesCount
															&& result.AllTrades.WinningTrades.TradesCount == r.AllTrades.WinningTrades.TradesCount).ToList();

				if (results.Count == 0)
					continue;

				results.Sort((a, b) => (a.ParameterValues[0] as NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic).NumNodes.CompareTo((b.ParameterValues[0] as NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic).NumNodes));

				if (!uniqueResults.Contains(results[0]))
					uniqueResults.Add(results[0]);
			}

			return uniqueResults;
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Generations							= 50;
				GenerationSize						= 100;
				Name								= Custom.Resource.NinjaScriptStrategyGenerator;
				ThresholdGenerations				= 10;
				UseCandleStickPatternForEntries		= true;
				UseCandleStickPatternForExits		= true;
				UseDayOfWeekForEntries				= false;
				UseDayOfWeekForExits				= false;
				UseIndicatorsForEntries				= true;
				UseIndicatorsForExits				= true;
				UseParabolicStopForExits			= true;
				UseSessionCloseForExits				= false;
				UseSessionTimeForEntries			= false;
				UseSessionTimeForExits				= false;
				UseStopTargetsForExits				= true;
			}
			else if (State == State.Configure)
			{
				if (oldKeepBestResults < 0)																	// make sure to only save the original .KeepBestResults value once 
					oldKeepBestResults				= KeepBestResults;										// (it's modified and copied to clone instances later on)

				KeepBestResults						= GenerationSize;										// needed all results for MonteCarlo
				NumberOfIterations					= Generations * GenerationSize;

				if (SelectedCandleStickPattern.Length == 0)
				{
					UseCandleStickPatternForEntries	= false;
					UseCandleStickPatternForExits	= false;
				}

				if (SelectedIndicatorTypes.Length == 0)
				{
					UseIndicatorsForEntries			= false;
					UseIndicatorsForExits			= false;
				}
			}
			else if (State == State.Terminated && oldKeepBestResults > 0)
				KeepBestResults = oldKeepBestResults;	
		}

		// here is how the GA works:
		// - after running a backtest on all individuals of a generation results are filtered for 'uniqueness' in regard to their .PerformanceValue
		// - next the population is split up in 5 groups of 'similar' size:
		//		* stable individuals: the best results, which (preferrably) are not re-run on next generation (since they should yield the same result still)
		//		* individuals which are mutated stable individuals: one or more properties are altered
		//		* individuals which are mutated instances of themselves: one or more properties are altered
		//		* individuals mutated by crossover: they get one or more properties of a stable individual
		//		* new random individuals
		protected override void OnOptimize()
		{		
			if (!OptimizeEntries && !OptimizeExits)
				throw new ArgumentException(Custom.Resource.NinjaScriptStrategyGeneratorEntriesOrExits);

			List<Cbi.SystemPerformance>	bestResults					= new List<Cbi.SystemPerformance>();
			double						bestResultsAverage			= double.MinValue;
			int							generationsSinceBestResult	= 0;
			List<NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic> population					= new List<NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic>();
			Random						random						= new Random();									// use only 1 instance to maximize randomness
			bool						resetAll					= true;
			int							numStable					= (int) (GenerationSize * 0.2);
			int							numMutatedStable			= Math.Max(0, Math.Min(GenerationSize - numStable, (int) (GenerationSize * 0.2)));
			int							numMutated					= Math.Max(0, Math.Min(GenerationSize - numStable - numMutatedStable, (int) (GenerationSize * 0.2)));
			int							numCrossOver				= Math.Max(0, Math.Min(GenerationSize - numStable - numMutatedStable - numMutated, (int) (GenerationSize * 0.2)));
			List<string>				uniqueIndividuals			= new List<string>();
			
			Strategies[0].IncludeTradeHistoryInBacktest				= true;											// needed trade history for Monte Carlo
			Strategies[0].IsInstantiatedOnEachOptimizationIteration	= true;											// optimization makes no sense in this scenario
			Strategies[0].SupportsOptimizationGraph					= false;										// not needed

			for (int generation = 0; ; generation++)
			{
				if (generation == 0)
					for (int k = 0; k < GenerationSize; k++)
						population.Add(new NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic { StrategyGenerator = this }.NewRandom(random));
				else
				{
					List<Cbi.SystemPerformance>		filteredResults		= new List<Cbi.SystemPerformance>();
					Cbi.SystemPerformance			oldPerformance		= Strategies[0].SystemPerformance;
					double							performance			= 0;
					List<Cbi.SystemPerformance>		uniqueResults		= GetUniqueResults();

					if (!Strategies[0].IsAggregated)
					{
						uniqueResults.Sort((a, b) => b.PerformanceValue.CompareTo(a.PerformanceValue));
						if (Progress != null)
						{
							for (int k = 0; k < Math.Min(oldKeepBestResults, uniqueResults.Count); k++)
								performance += uniqueResults[k].PerformanceValue;

							Progress.Message = string.Format(Custom.Resource.NinjaScriptStrategyGeneratorPeformance, Strategies[0].Instrument.FullName, (performance / Math.Min(oldKeepBestResults, uniqueResults.Count)).ToString("#.00", Core.Globals.GeneralOptions.CurrentCulture));
						}
					}

					foreach (Cbi.SystemPerformance systemPerformance in uniqueResults)
					{
						// don't need MonteCarlo on last iteration
						if (generation < Generations - 1)
						{
							// https://www.dummies.com/education/math/statistics/how-to-calculate-a-confidence-interval-for-a-population-mean-when-you-know-its-standard-deviation/
							double						confidenceLevel		= 1.645;		// 95% confidence. This is not symmetric, so we're looking at 90% instead of 95%
							List<double>				monteCarloResults	= new List<double>();
							foreach (Cbi.SystemPerformance systemPerformance2 in new MonteCarlo { NumberOfTrades = systemPerformance.AllTrades.Count }.Run(systemPerformance.AllTrades, null))
							{
								Strategies[0].SystemPerformance = systemPerformance2;
								Strategies[0].OptimizationFitness.CalculatePerformanceValue(Strategies[0]);

								monteCarloResults.Add(Strategies[0].OptimizationFitness.Value);
							}

							if (monteCarloResults.Count == 0)
								continue;

							double mean			= monteCarloResults.Sum() / monteCarloResults.Count;
							double stdDev		= Math.Sqrt(monteCarloResults.Sum(r => (r - mean) * (r - mean)) / monteCarloResults.Count);
							systemPerformance.PerformanceValue = mean - confidenceLevel * stdDev / Math.Sqrt(systemPerformance.AllTrades.Count);

							if (systemPerformance.PerformanceValue.ApproxCompare((systemPerformance.ParameterValues[0] as NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic).PriorPerformance) < 0)
								(systemPerformance.ParameterValues[0] as NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic).TryLinearMutation = false;
						}

						(systemPerformance.ParameterValues[0] as NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic).PriorPerformance = systemPerformance.PerformanceValue;
						filteredResults.Add(systemPerformance);
					}
					filteredResults.Sort((a, b) => b.PerformanceValue.CompareTo(a.PerformanceValue));
					Strategies[0].SystemPerformance = oldPerformance;

					bool	getOut					= generation >= Generations;
					double	resultAverage			= 0;
					for (int k = 0; k < Math.Min(numStable, filteredResults.Count); k++)
						resultAverage += filteredResults[k].PerformanceValue;
					
					if (filteredResults.Count == 0 || resultAverage / Math.Min(numStable, filteredResults.Count) < bestResultsAverage)
					{
						if (ThresholdGenerations > 0 && ++generationsSinceBestResult > ThresholdGenerations - 1)
						{
							Log(string.Format(Custom.Resource.NinjaScriptStrategyGeneratorTerminated, generation, ThresholdGenerations), Cbi.LogLevel.Information);
							getOut = true;
						}
					}
					else if (filteredResults.Count > 0)
					{
						bestResultsAverage			= resultAverage / Math.Min(numStable, filteredResults.Count);
						generationsSinceBestResult	= 0;

						bestResults.Clear();
						foreach (Cbi.SystemPerformance tmp in uniqueResults)
						{
							Cbi.SystemPerformance savePerformance = new Cbi.SystemPerformance(false);
							tmp.CopyPerformance(savePerformance);
							savePerformance.ParameterValues[0] = (tmp.ParameterValues[0] as NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic).Clone();
							bestResults.Add(savePerformance);
						}
					}

					if (getOut)
					{
						Results = bestResults.ToArray();
						break;
					}

					// just re-run all individuals in case there had been 'duplicate' or filtered results
					resetAll = (filteredResults.Count < Results.Length);

					// keep all individuals which are 'stable'
					List<NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic> newPopulation	= new List<NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic>();
					for (int k = 0; k < Math.Min(numStable, filteredResults.Count); k++)
						newPopulation.Add(filteredResults[k].ParameterValues[0] as NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic);

					// append all remaining individuals = those which are not 'stable' (and will be changed/replaced by mutation or crossover or new, random generation)
					long[] stableIds = newPopulation.Select(p => p.Id).ToArray();
					newPopulation.AddRange(filteredResults.Where(f => !stableIds.Contains((f.ParameterValues[0] as NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic).Id)).Select(p => (p.ParameterValues[0] as NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic)).ToArray());

					// fill up the population in case results had been removed
					while (newPopulation.Count < GenerationSize)
					{
						NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic individual = null;
						while (uniqueIndividuals.Contains((individual = new NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic { StrategyGenerator = this }.NewRandom(random)).ToString())) {}
						newPopulation.Add(individual);
					}

					population = newPopulation;

					// keep the stable individuals. No point to re-run a backtest 
					Reset(resetAll ? 0 : numStable);

					for (int k = 0; k < population.Count; k++)
					{
						NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic individual = null;
						if (k < numStable)																// stable individuals
						{ 
							if (resetAll)
								population[k] = population[k].Clone() as NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic;

							individual = population[k];
						}
						else if (k < numStable + numMutatedStable)                                      // stable individuals to mutate
						{
							for (int i = 0; uniqueIndividuals.Contains((individual = population[k - numStable].NewMutation(random)).ToString()); i++)
							{
								// give up and try a completely new individual
								if (i == 10)
								{
									while (uniqueIndividuals.Contains((individual = new NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic { StrategyGenerator = this }.NewRandom(random)).ToString())) {}
									break;
								}

								population[k - numStable].TryLinearMutation = false;					// linear mutation yields the same individual -> disable
							}
						}
						else if (k < numStable + numMutatedStable + numMutated)                         // individuals to mutate
						{
							for (int i = 0; uniqueIndividuals.Contains((individual = population[k].NewMutation(random)).ToString()); i++)
							{
								// give up and try a completely new individual
								if (i == 10)
								{
									while (uniqueIndividuals.Contains((individual = new NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic { StrategyGenerator = this }.NewRandom(random)).ToString())) {}
									break;
								}

								population[k].TryLinearMutation = false;								// linear mutation yields the same individual -> disable
							}
						}
						else if (k < numStable + numMutatedStable + numMutated + numCrossOver)			// individuals to mutate by crossover
						{
							for (int m = 0; ; m++)
							{
								NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic fitter = population[random.Next(numStable)];
								
								// find a stable individual with similar long/short pattern
								if (population[k].IsLong == fitter.IsLong && population[k].IsShort == fitter.IsShort)
								{
									for (int j = 0; ; j++)				// crossover and try to create a new individual
									{
										if (!uniqueIndividuals.Contains((individual = population[k].NewCrossOver(fitter, random)).ToString()))
											break;
										else if (j >= GenerationSize)	// no success -> inject a new random indivdual
										{
											while (uniqueIndividuals.Contains((individual = new NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic { StrategyGenerator = this }.NewRandom(random)).ToString())) {}
											break;
										}
									}
									break;
								}
								else if (m >= numStable) // no success -> inject a new random indivdual
								{
									while (uniqueIndividuals.Contains((individual = new NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic { StrategyGenerator = this }.NewRandom(random)).ToString())) {}
									break;
								}
							}
						}
						else																							// new random individuals
							while (uniqueIndividuals.Contains((individual = new NinjaTrader.NinjaScript.StrategyGenerator.GeneratedStrategyLogic { StrategyGenerator = this }.NewRandom(random)).ToString())) {}

						uniqueIndividuals.Add(individual.ToString());
						population[k] = individual;
					}
				}

				for (int k = 0; k < population.Count; k++)
				{
					if (!resetAll && generation > 0 && k < Math.Min(oldKeepBestResults, numStable))
					{
						if (Progress != null)
							Progress.PerformStep();
					}
					else
					{
						Strategies[0].GeneratedStrategyLogic = population[k];
						RunIteration();
					}
				}

				WaitForIterationsCompleted();

				if (Progress != null && Progress.IsAborted)
				{
					Results = bestResults.ToArray();
					break;
				}
			}

			List<Cbi.SystemPerformance>	tmpResults	= GetUniqueResults();
			KeepBestResults							= oldKeepBestResults;
			Results									= new Cbi.SystemPerformance[Math.Min(KeepBestResults, tmpResults.Count)];
			tmpResults.Sort((a, b) => b.PerformanceValue.CompareTo(a.PerformanceValue));
			Array.Copy(tmpResults.ToArray(), Results, Math.Min(KeepBestResults, tmpResults.Count));
		}

#region UI Properties
		[Display(ResourceType = typeof(Custom.Resource), GroupName = "NinjaScriptStrategyGeneratorProperties", Name = "NinjaScriptGeneticOptimizerGenerations", Order = 40)]
		[Range(1, Int32.MaxValue)]
		public int Generations
		{ get; set; }

		[Display(ResourceType = typeof(Custom.Resource), GroupName = "NinjaScriptStrategyGeneratorProperties", Name = "NinjaScriptGeneticOptimizerGenerationSize", Order = 50)]
		[Range(1, Int32.MaxValue)]
		public int GenerationSize
		{ get; set; }

		public bool OptimizeEntries
		{
			get { return UseCandleStickPatternForEntries || UseDayOfWeekForEntries || UseIndicatorsForEntries || UseSessionTimeForEntries; }
		}

		public bool OptimizeExits
		{
			get { return UseCandleStickPatternForExits || UseDayOfWeekForExits || UseIndicatorsForExits || UseParabolicStopForExits || UseSessionTimeForExits || UseStopTargetsForExits || UseSessionCloseForExits ; }
		}

		[Gui.PropertyEditor("NinjaTrader.Gui.Tools.AvailableCandleStickPatternListEditor")]
		[Display(ResourceType = typeof(Custom.Resource), GroupName = "NinjaScriptStrategyGeneratorProperties", Name = "NinjaScriptStrategyGeneratorUseCandleStickPattern", Order = 10, Prompt = "NinjaScriptStrategyGeneratorCandleStickPatternPrompt")]
		public ChartPattern[] SelectedCandleStickPattern
		{
			get
			{
				if (selectedCandleStickPattern == null)
					selectedCandleStickPattern = AvailableCandleStickPattern;

				return selectedCandleStickPattern;
			}
			set
			{
				selectedCandleStickPattern = value;
			}
		}

		[Gui.PropertyEditor("NinjaTrader.Gui.Tools.AvailableIndicatorsListEditor")]
		[Display(ResourceType = typeof(Custom.Resource), GroupName = "NinjaScriptStrategyGeneratorProperties", Name = "NinjaScriptStrategyGeneratorUseIndicators", Order = 0, Prompt = "NinjaScriptStrategyGeneratorIndicatorsPrompt")]
		public Type[] SelectedIndicatorTypes
		{
			get
			{
				if (selectedIndicatorTypes == null)
					selectedIndicatorTypes = AvailableIndicators.Keys.ToArray();

				return selectedIndicatorTypes;
			}
			set
			{
				selectedIndicatorTypes = value;
			}
		}

		/// <summary>
		/// Abort if for N generations there was no improvement on the average performance of the 'best results to keep'. Set to '0' to disable
		/// </summary>
		[Display(ResourceType = typeof(Custom.Resource), GroupName = "NinjaScriptStrategyGeneratorProperties", Name = "NinjaScriptGeneticOptimizerThresholdGenerations", Order = 60)]
		[Range(0, Int32.MaxValue)]
		public int ThresholdGenerations
		{ get; set; }

		[Browsable(false)]
		public bool UseCandleStickPatternForEntries
		{ get; set; }

		[Browsable(false)]
		public bool UseCandleStickPatternForExits
		{ get; set; }

		[Display(ResourceType = typeof(Resource), GroupName = "NinjaScriptStrategyGeneratorEntries", Name = "NinjaScriptStrategyGeneratorDayOfWeek", Order = 20)]
		public bool UseDayOfWeekForEntries
		{ get; set; }

		[Display(ResourceType = typeof(Resource), GroupName = "NinjaScriptStrategyGeneratorExits", Name = "NinjaScriptStrategyGeneratorDayOfWeek", Order = 2)]
		public bool UseDayOfWeekForExits
		{ get; set; }

		[Browsable(false)]
		public bool UseIndicatorsForEntries
		{ get; set; }

		[Browsable(false)]
		public bool UseIndicatorsForExits
		{ get; set; }

		[Display(ResourceType = typeof(Resource), GroupName = "NinjaScriptStrategyGeneratorExits", Name = "NinjaScriptStrategyGeneratorUseParabolicStop", Order = 4)]
		public bool UseParabolicStopForExits
		{ get; set; }

		[Display(ResourceType = typeof(Resource), GroupName = "NinjaScriptStrategyGeneratorExits", Name = "NinjaScriptStrategyGeneratorUseSessionClose", Order = 6)]
		public bool UseSessionCloseForExits
		{ get; set; }

		[Display(ResourceType = typeof(Resource), GroupName = "NinjaScriptStrategyGeneratorEntries", Name = "NinjaScriptStrategyGeneratorUseSessionTime", Order = 30)]
		public bool UseSessionTimeForEntries
		{ get; set; }

		[Display(ResourceType = typeof(Resource), GroupName = "NinjaScriptStrategyGeneratorExits", Name = "NinjaScriptStrategyGeneratorUseSessionTime", Order = 3)]
		public bool UseSessionTimeForExits
		{ get; set; }

		[Display(ResourceType = typeof(Resource), GroupName = "NinjaScriptStrategyGeneratorExits", Name = "NinjaScriptStrategyGeneratorUseStopTargets", Order = 5)]
		public bool UseStopTargetsForExits
		{ get; set; }
#endregion
	}
}

namespace NinjaTrader.NinjaScript.StrategyGenerator
{
	internal static class Extensions
	{
		internal static void Indent(this StringBuilder stringBuilder, int tabLevels)
		{
			for (int i = 0; i < tabLevels; i++)
				stringBuilder.Append('\t');
		}
	}

	internal enum LogicalOperator
	{
		And,
		Not,
		Or,
	}

	internal interface IExpression : ICloneable
	{
		bool						Evaluate(GeneratedStrategyLogic logic, StrategyBase strategy);
		List<IExpression>			GetExpressions();
		void						Initialize(StrategyBase strategy);
		IExpression					NewMutation(GeneratedStrategyLogic logic, Random random, IExpression toMutate);
		void						Print(StringBuilder stringBuilder, int indentationLevel);
		void						PrintAddChartIndicator(GeneratedStrategyLogic logic, StringBuilder stringBuilder, int indentationLevel);
		XElement					ToXml();
	}

	// for strategies which (optionally) implement this interface the respective methods below would be called instead of just EnterLong/EnterShort/ExitLong/ExitShort
	// this allows for applying custom enter/exit logic including e.g. additional filters or checks
	public interface IGeneratedStrategy
	{
		Cbi.Order					OnEnterLong();
		Cbi.Order					OnEnterShort();
		Cbi.Order					OnExitLong();
		Cbi.Order					OnExitShort();
	}

	internal class CandleStickPatternExpression : IExpression
	{
		public CandleStickPatternExpression()
		{
			Pattern = ChartPattern.MorningStar;
		}

		public object Clone()
		{
			return new CandleStickPatternExpression { Pattern = Pattern };
		}

		public bool Evaluate(GeneratedStrategyLogic u, StrategyBase s)
		{
			return u.GetCandleStickPatternLogic(s).Evaluate(Pattern);
		}

		public static IExpression FromXml(XElement element)
		{
			return new CandleStickPatternExpression { Pattern = (ChartPattern) Enum.Parse(typeof(ChartPattern), element.Element("Pattern").Value) };
		}

		public List<IExpression> GetExpressions()
		{
			return new List<IExpression>(new IExpression[] { this });
		}

		public void Initialize(StrategyBase strategy)
		{
			// nothing to do here
		}

		public IExpression NewMutation(GeneratedStrategyLogic logic, Random random, IExpression toMutate)
		{
			return new CandleStickPatternExpression { Pattern = logic.RandomCandleStickPattern(random) };
		}

		public ChartPattern Pattern
		{ get; set; }

		public void Print(StringBuilder s, int indentationLevel)
		{
			s.Append("candleStickPatternLogic.Evaluate(ChartPattern." + Pattern.ToString() + ")");
		}

		public void PrintAddChartIndicator(GeneratedStrategyLogic logic, StringBuilder s, int indentationLevel)
		{
			string text = "AddChartIndicator(CandlestickPattern(ChartPattern." + Pattern + ", " + logic.TrendStrength + "));" + Environment.NewLine;
			if (!logic.ChartIndicators.Contains(text))
			{
				logic.ChartIndicators.Add(text);
				s.Indent(indentationLevel);
				s.Append(text);
			}
		}

		public XElement ToXml()
		{
			XElement ret = new XElement(GetType().Name);

			ret.Add(new XElement("Pattern", Pattern.ToString()));

			return ret;
		}
	}

	internal class IndicatorExpression : IExpression
	{
		private		int			r0			= -1;
		private		int			r1			= -1;
		private		int			r2			= -1;
		private		int			r3			= -1;
		private		int			r4			= -1;

		public object Clone()
		{
			IndicatorBase left	= (IndicatorBase) Left.Clone();
			IndicatorBase right = (IndicatorBase) Right.Clone();

			try 
			{
				left.SetState(State.Configure);
			}
			catch (Exception exp)
			{
				Cbi.Log.Process(typeof(Resource), "CbiUnableToCreateInstance2", new object[] { Left.Name, exp.InnerException != null ? exp.InnerException.ToString() : exp.ToString() }, Cbi.LogLevel.Error, Cbi.LogCategories.Default);
				left.SetState(State.Finalized);
			}			
			
			try 
			{
				right.SetState(State.Configure);
			}
			catch (Exception exp)
			{
				Cbi.Log.Process(typeof(Resource), "CbiUnableToCreateInstance2", new object[] { Right.Name, exp.InnerException != null ? exp.InnerException.ToString() : exp.ToString() }, Cbi.LogLevel.Error, Cbi.LogCategories.Default);
				right.SetState(State.Finalized);
			}			

			left.SelectedValueSeries	= Left.SelectedValueSeries;			// had to take care here since not copied by cloning
			right.SelectedValueSeries	= Right.SelectedValueSeries;

			return new IndicatorExpression { CompareFactor = CompareFactor, Condition = Condition, Left = left, LeftBarsAgo = LeftBarsAgo,
								MaxCompare = MaxCompare, MinCompare = MinCompare,
								r0 = r0, r1 = r1, r2 = r2, r3 = r3, r4 = r4,
								Right = right, RightBarsAgo = RightBarsAgo, UsePriceToCompare = UsePriceToCompare };
		}

		/// <summary>
		/// Between 0..1
		/// </summary>
		public double CompareFactor
		{ get; set; }

		private double CompareValue
		{ 
			get { return MinCompare + (MaxCompare - MinCompare) * CompareFactor; }
		}

		public Condition Condition
		{ get; set; }

		public bool Evaluate(GeneratedStrategyLogic logic, StrategyBase strategy)
		{
			try
			{
				switch (Condition)
				{
					case Condition.CrossAbove:
					{
						int		aboveIdx		= -1;
						int		lookBackPeriod	= 1;

						if (Left.IsOverlay)
						{
							ISeries<double>	series	= (UsePriceToCompare ? Left.Close : Right.Values[Right.SelectedValueSeries]);
							int				maxBack	= Math.Min(series.Count - 1, Math.Min(lookBackPeriod, Left.Count - 1));
							for (int idx = 0; idx <= maxBack; idx++)
							{
								if (aboveIdx < 0 && Left.Values[Left.SelectedValueSeries][idx] > series[idx])
									aboveIdx = idx;
								else if (aboveIdx >= 0 && Left.Values[Left.SelectedValueSeries][idx] <= series[idx])
									return true;
							}
						}
						else
						{
							int	maxBack = Math.Min(lookBackPeriod, Left.Count - 1);
							for (int idx = 0; idx <= maxBack; idx++)
							{
								if (aboveIdx < 0 && Left.Values[Left.SelectedValueSeries][idx] > CompareValue)
									aboveIdx = idx;
								else if (aboveIdx >= 0 && Left.Values[Left.SelectedValueSeries][idx] <= CompareValue)
									return true;
							}
						}

						return false;
					}
					case Condition.CrossBelow:
					{
						int		belowIdx		= -1;
						int		lookBackPeriod	= 1;

						if (Left.IsOverlay)
						{
							ISeries<double>	series	= (UsePriceToCompare ? Left.Close : Right.Values[Right.SelectedValueSeries]);
							int				maxBack	= Math.Min(series.Count - 1, Math.Min(lookBackPeriod, Left.Count - 1));
							for (int idx = 0; idx <= maxBack; idx++)
							{
								if (belowIdx < 0 && Left.Values[Left.SelectedValueSeries][idx] < series[idx])
									belowIdx = idx;
								else if (belowIdx >= 0 && Left.Values[Left.SelectedValueSeries][idx] >= series[idx])
									return true;
							}
						}
						else
						{
							int	maxBack = Math.Min(lookBackPeriod, Left.Count - 1);
							for (int idx = 0; idx <= maxBack; idx++)
							{
								if (belowIdx < 0 && Left.Values[Left.SelectedValueSeries][idx] < CompareValue)
									belowIdx = idx;
								else if (belowIdx >= 0 && Left.Values[Left.SelectedValueSeries][idx] >= CompareValue)
									return true;
							}
						}
						return false;
					}
					case Condition.Equals:			return Left.Values[Left.SelectedValueSeries][LeftBarsAgo].ApproxCompare(Left.IsOverlay ? (UsePriceToCompare ? Left.Close[RightBarsAgo] : Right.Values[Right.SelectedValueSeries][RightBarsAgo]) : CompareValue) == 0;
					case Condition.Greater:			return Left.Values[Left.SelectedValueSeries][LeftBarsAgo].ApproxCompare(Left.IsOverlay ? (UsePriceToCompare ? Left.Close[RightBarsAgo] : Right.Values[Right.SelectedValueSeries][RightBarsAgo]) : CompareValue) > 0;
					case Condition.GreaterEqual:	return Left.Values[Left.SelectedValueSeries][LeftBarsAgo].ApproxCompare(Left.IsOverlay ? (UsePriceToCompare ? Left.Close[RightBarsAgo] : Right.Values[Right.SelectedValueSeries][RightBarsAgo]) : CompareValue) >= 0;
					case Condition.Less:			return Left.Values[Left.SelectedValueSeries][LeftBarsAgo].ApproxCompare(Left.IsOverlay ? (UsePriceToCompare ? Left.Close[RightBarsAgo] : Right.Values[Right.SelectedValueSeries][RightBarsAgo]) : CompareValue) < 0;
					case Condition.LessEqual:		return Left.Values[Left.SelectedValueSeries][LeftBarsAgo].ApproxCompare(Left.IsOverlay ? (UsePriceToCompare ? Left.Close[RightBarsAgo] : Right.Values[Right.SelectedValueSeries][RightBarsAgo]) : CompareValue) <= 0;
					case Condition.NotEqual:		return Left.Values[Left.SelectedValueSeries][LeftBarsAgo].ApproxCompare(Left.IsOverlay ? (UsePriceToCompare ? Left.Close[RightBarsAgo] : Right.Values[Right.SelectedValueSeries][RightBarsAgo]) : CompareValue) != 0;
					default:						return false;
				}
			}
			catch
			{
				StringBuilder stringBuilder = new StringBuilder();
				Print(stringBuilder, 1);
				Cbi.Log.Process(typeof(Custom.Resource), "NinjaScriptStrategyGeneratorIndicatorException", new object[] { Environment.NewLine, stringBuilder.ToString() }, Cbi.LogLevel.Error, Cbi.LogCategories.Default);
				throw;
			}
		}

		public static IExpression FromXml(XElement element)
		{
			IndicatorExpression ret = new IndicatorExpression
										{
											CompareFactor		= double.Parse(element.Element("CompareFactor").Value, CultureInfo.InvariantCulture),
											Condition			= (Condition) Enum.Parse(typeof(Condition), element.Element("Condition").Value),
											LeftBarsAgo			= int.Parse(element.Element("LeftBarsAgo").Value),
											MaxCompare			= double.Parse(element.Element("MaxCompare").Value, CultureInfo.InvariantCulture),
											MinCompare			= double.Parse(element.Element("MinCompare").Value, CultureInfo.InvariantCulture),
											RightBarsAgo		= int.Parse(element.Element("RightBarsAgo").Value),
											UsePriceToCompare	= bool.Parse(element.Element("UsePriceToCompare").Value),
										};

			if (element.Element("LeftType") != null)
				ret.Left = new XmlSerializer(Core.Globals.AssemblyRegistry.GetType(element.Element("LeftType").Value)).Deserialize(element.Element("Left").FirstNode.CreateReader()) as IndicatorBase;

			if (element.Element("RightType") != null)
				ret.Right = new XmlSerializer(Core.Globals.AssemblyRegistry.GetType(element.Element("RightType").Value)).Deserialize(element.Element("Right").FirstNode.CreateReader()) as IndicatorBase;

			return ret;
		}
		
		public List<IExpression> GetExpressions()
		{
			return new List<IExpression>(new IExpression[] { this });
		}

		public IndicatorExpression()
		{
			MaxCompare = double.NaN;
			MinCompare = double.NaN;
		}

		public void Initialize(StrategyBase strategy)
		{
			Left.Parent		= strategy;
			Right.Parent	= strategy;
			Left.SetInput(strategy.Input);
			Right.SetInput(strategy.Input);

			lock (strategy.NinjaScripts)
			{
				strategy.NinjaScripts.Add(Left);
				strategy.NinjaScripts.Add(Right);
			}

			try
			{
				Left.SetState(strategy.State);
			}
			catch (Exception exp)
			{
				Cbi.Log.Process(typeof(Resource), "CbiUnableToCreateInstance2", new object[] { Left.Name, exp.InnerException != null ? exp.InnerException.ToString() : exp.ToString() }, Cbi.LogLevel.Error, Cbi.LogCategories.Default);
				Left.SetState(State.Finalized);
				return;
			}

			try
			{
				Right.SetState(strategy.State);
			}
			catch (Exception exp)
			{
				Cbi.Log.Process(typeof(Resource), "CbiUnableToCreateInstance2", new object[] { Right.Name, exp.InnerException != null ? exp.InnerException.ToString() : exp.ToString() }, Cbi.LogLevel.Error, Cbi.LogCategories.Default);
				Right.SetState(State.Finalized);
				return;
			}

			if (!Left.IsOverlay && double.IsNaN(MaxCompare))
			{
				Left.Update(Left.BarsArray[0].Count - 1, 0);

				MaxCompare = double.MinValue;
				MinCompare = double.MaxValue;
				for (int i = 0; i < Left.BarsArray[0].Count; i++)									// find min/max range, .CompareValue will be in between
				{
					MaxCompare = Math.Max(MaxCompare,	Left.Values[Left.SelectedValueSeries].GetValueAt(i));
					MinCompare = Math.Min(MinCompare,	Left.Values[Left.SelectedValueSeries].GetValueAt(i));
				}

				MaxCompare = RoundToNearestDecimal(MaxCompare, true);
				MinCompare = RoundToNearestDecimal(MinCompare, false);
			}
		}
		
		public IndicatorBase Left
		{ get; set; }

		public int LeftBarsAgo
		{ get; set; }

		public double MaxCompare
		{ get; set; }

		public double MinCompare
		{ get; set; }

		public IExpression NewMutation(GeneratedStrategyLogic logic, Random random, IExpression toMutate)
		{
			IndicatorExpression	ret = null;
			while (true)
			{
				if (!logic.TryLinearMutation)
				{
					// < 2:		mutate .Condition
					// < 4:		mutate .Left
					// < 6:		mutate .Right/.UsePriceToCompare or .CompareValue
					// < 8:		mutate .Left's .SelectedValueSeries
					// < 10:	mutate .Right's .SelectedValueSeries
					// < 20:	mutate random .Left property
					// < 30:	mutate random .Right property
					// < 40:	mutate .LeftBarsAgo between 0 and 9
					// < 50:	mutate .RightBarsAgo between 0 and 9
					r0 = random.Next(50);

					// 0: increment
					// 1: decrement
					r2 = random.Next(2);

					r3 = random.Next(GeneratedStrategyLogic.NumConditions);
				}

				ret = new IndicatorExpression
				{
					CompareFactor	= CompareFactor,
					Condition		= (toMutate == this && r0 >= 0 && r0 < 2 ? (Condition) r3 : Condition),
					Left			= (toMutate == this && r0 >= 2 && r0 < 4 ? logic.RandomIndicator(random) : (IndicatorBase) Left.Clone()),
					LeftBarsAgo		= LeftBarsAgo,
					MaxCompare		= double.NaN,
					MinCompare		= double.NaN,
					Right			= (toMutate == this && r0 >= 4 && r0 < 6 ? logic.RandomIndicator(random) : (IndicatorBase) Right.Clone()),
					RightBarsAgo	= RightBarsAgo,
				};

				Tuple<double, double> minMax = null;
				if (Optimizers.StrategyGenerator.AvailableIndicators.TryGetValue(ret.Left.GetType(), out minMax) && minMax != null)
				{
					ret.MinCompare = minMax.Item1;
					ret.MaxCompare = minMax.Item2;
				}

				if (ret.Left.IsOverlay)
					while (!ret.Right.IsOverlay)
					{
						try { ret.Right.SetState(State.Finalized); } catch {}
						ret.Right = logic.RandomIndicator(random);
					}
				else
					while (ret.Right.IsOverlay)
					{
						try { ret.Right.SetState(State.Finalized); } catch {}
						ret.Right = logic.RandomIndicator(random);
					}

				// make sure .State >= .Configure
				try
				{
					ret.Left.SetState(State.Configure);
				}
				catch (Exception exp)
				{
					Cbi.Log.Process(typeof(Resource), "CbiUnableToCreateInstance2", new object[] { ret.Left.Name, exp.InnerException != null ? exp.InnerException.ToString() : exp.ToString() }, Cbi.LogLevel.Error, Cbi.LogCategories.Default);
					ret.Left.SetState(State.Finalized);
					return ret;
				}

				try
				{
					ret.Right.SetState(State.Configure);
				}
				catch (Exception exp)
				{
					Cbi.Log.Process(typeof(Resource), "CbiUnableToCreateInstance2", new object[] { ret.Right.Name, exp.InnerException != null ? exp.InnerException.ToString() : exp.ToString() }, Cbi.LogLevel.Error, Cbi.LogCategories.Default);
					ret.Left.SetState(State.Finalized);
					ret.Right.SetState(State.Finalized);
					return ret;
				}

				if (toMutate == this)
				{
					if (r0 >= 2 && r0 < 4)
					{
						ret.MaxCompare = double.NaN;
						ret.MinCompare = double.NaN;
					}
					else if (r0 >= 4 && r0 < 6)
					{
						if (!double.IsNaN(ret.MaxCompare))
							ret.CompareFactor = Math.Min(1, Math.Max(0, ret.CompareFactor + (r2 == 0 ? 0.1 : -0.1)));

						ret.UsePriceToCompare = (r2 == 0);
					}
					else if (r0 >= 10 && r0 < 30)
					{
						IndicatorBase	indicator = (r0 < 20 ? ret.Left : ret.Right);
						double			value;

						List<PropertyInfo> properties = indicator.GetType().GetProperties().Where(p => Attribute.GetCustomAttribute(p, typeof(RangeAttribute), false) != null
																									&& Attribute.GetCustomAttribute(p, typeof(NinjaScriptPropertyAttribute), false) != null).ToList();
						if (properties.Count == 0 || indicator.State == State.Finalized)
							return ret;

						if (!logic.TryLinearMutation)
							r1 = random.Next(properties.Count);

						logic.TryLinearMutation = true;

						PropertyInfo propertyInfo = properties[r1];
						try
						{
							value = (double) Convert.ChangeType(propertyInfo.GetValue(indicator, null), typeof(double));
						}
						catch (Exception exp)
						{
							indicator.LogAndPrint(typeof(Resource), "DataGetPropertyValueException", new object[] { propertyInfo.Name, indicator.Name, NinjaScriptBase.GetExceptionMessage(exp) }, Cbi.LogLevel.Error); 
							indicator.SetState(State.Finalized);
							return ret;
						}

						RangeAttribute	rangeAttribute	= Attribute.GetCustomAttribute(propertyInfo, typeof(RangeAttribute), false) as RangeAttribute;
						double			maximum			= (double) Convert.ChangeType(rangeAttribute.Maximum, typeof(double));
						double			minimum			= (double) Convert.ChangeType(rangeAttribute.Minimum, typeof(double));

						// make sure we're not looking back further than MaximumBarsLookBack.TwoHundredFiftySix
						// this works off the assumption that all 'period' properties are 'int' and all 'int' properties should not reasonably exceed the value range of 256 - N
						if (propertyInfo.PropertyType == typeof(int))
						{
							maximum = 256 - 10;							// just have N = 10
							value	= value + (r2 == 0 ? 1 : -1);
						}
						else
						{
							maximum	= 50.000;							// some 'random' limitation
							value	= value * (r2 == 0 ? 1.25 : 0.75);
						}

						try
						{
							value = Math.Max(minimum, Math.Min(maximum, value));

							propertyInfo.SetValue(indicator, Convert.ChangeType(value, propertyInfo.PropertyType));
						}
						catch (Exception exp)
						{
							indicator.LogAndPrint(typeof(Resource), "DataGetPropertyValueException", new object[] { propertyInfo.Name, indicator.Name, NinjaScriptBase.GetExceptionMessage(exp) }, Cbi.LogLevel.Error); 
							indicator.SetState(State.Finalized);
							return ret;
						}
					}
					else if (r0 >= 6 && r0 < 10)
					{
						if (!logic.TryLinearMutation)
							r4 = random.Next((r0 < 8 ? ret.Left : ret.Right).Values.Length);

						(r0 < 8 ? ret.Left : ret.Right).SelectedValueSeries = r4;
				
						ret.MaxCompare = double.NaN;
						ret.MinCompare = double.NaN;
					}
					else if (r0 >= 30 && r0 < 40)
					{
						ret.LeftBarsAgo						= Math.Max(0, ret.LeftBarsAgo + (r2 == 0 ? 1 : -1));
						logic.TryLinearMutation	= true;
					}
					else if (r0 >= 40 && r0 < 50)
					{
						ret.RightBarsAgo					= Math.Max(0, ret.RightBarsAgo + (r2 == 0 ? 1 : -1));
						logic.TryLinearMutation	= true;
					}
				}

				return ret;
			}
		}

		public void Print(StringBuilder s, int indentationLevel)
		{
			switch (Condition)
			{
				case Condition.CrossAbove:
					s.Append("CrossAbove(");
					s.Append(Left.GetDisplayName(true, true, false));
					if (Left.SelectedValueSeries != 0)
						s.Append(".Values[" + Left.SelectedValueSeries + "]");
					s.Append(", ");
					s.Append(Left.IsOverlay ? (UsePriceToCompare ? "Close" : Right.GetDisplayName(true, true, false) + (Right.SelectedValueSeries != 0 ? ".Values[" + Right.SelectedValueSeries + "]" : string.Empty)) : CompareValue.ToString(CultureInfo.InvariantCulture));
					s.Append(", 1)");
					break;
				
				case Condition.CrossBelow:
					s.Append("CrossBelow(");
					s.Append(Left.GetDisplayName(true, true, false));
					if (Left.SelectedValueSeries != 0)
						s.Append(".Values[" + Left.SelectedValueSeries + "]");
					s.Append(", ");
					s.Append(Left.IsOverlay ? (UsePriceToCompare ? "Close" : Right.GetDisplayName(true, true, false) + (Right.SelectedValueSeries != 0 ? ".Values[" + Right.SelectedValueSeries + "]" : string.Empty)) : CompareValue.ToString(CultureInfo.InvariantCulture));
					s.Append(", 1)");
					break;

				case Condition.Equals:
					s.Append(Left.GetDisplayName(true, true, false));
					if (Left.SelectedValueSeries != 0)
						s.Append(".Values[" + Left.SelectedValueSeries + "]");
					s.Append("[" + LeftBarsAgo + "].ApproxCompare(");
					s.Append(Left.IsOverlay ? (UsePriceToCompare ? "Close" : Right.GetDisplayName(true, true, false) + (Right.SelectedValueSeries != 0 ? ".Values[" + Right.SelectedValueSeries + "]" : string.Empty)) + "[" + RightBarsAgo + "]" : CompareValue.ToString(CultureInfo.InvariantCulture));
					s.Append(") == 0");
					break;

				case Condition.Greater:
					s.Append(Left.GetDisplayName(true, true, false));
					if (Left.SelectedValueSeries != 0)
						s.Append(".Values[" + Left.SelectedValueSeries + "]");
					s.Append("[" + LeftBarsAgo + "].ApproxCompare(");
					s.Append(Left.IsOverlay ? (UsePriceToCompare ? "Close" : Right.GetDisplayName(true, true, false) + (Right.SelectedValueSeries != 0 ? ".Values[" + Right.SelectedValueSeries + "]" : string.Empty)) + "[" + RightBarsAgo + "]" : CompareValue.ToString(CultureInfo.InvariantCulture));
					s.Append(") > 0");
					break;

				case Condition.GreaterEqual:
					s.Append(Left.GetDisplayName(true, true, false));
					if (Left.SelectedValueSeries != 0)
						s.Append(".Values[" + Left.SelectedValueSeries + "]");
					s.Append("[" + LeftBarsAgo + "].ApproxCompare(");
					s.Append(Left.IsOverlay ? (UsePriceToCompare ? "Close" : Right.GetDisplayName(true, true, false) + (Right.SelectedValueSeries != 0 ? ".Values[" + Right.SelectedValueSeries + "]" : string.Empty)) + "[" + RightBarsAgo + "]" : CompareValue.ToString(CultureInfo.InvariantCulture));
					s.Append(") >= 0");
					break;

				case Condition.Less:
					s.Append(Left.GetDisplayName(true, true, false));
					if (Left.SelectedValueSeries != 0)
						s.Append(".Values[" + Left.SelectedValueSeries + "]");
					s.Append("[" + LeftBarsAgo + "].ApproxCompare(");
					s.Append(Left.IsOverlay ? (UsePriceToCompare ? "Close" : Right.GetDisplayName(true, true, false) + (Right.SelectedValueSeries != 0 ? ".Values[" + Right.SelectedValueSeries + "]" : string.Empty)) + "[" + RightBarsAgo + "]" : CompareValue.ToString(CultureInfo.InvariantCulture));
					s.Append(") < 0");
					break;

				case Condition.LessEqual:
					s.Append(Left.GetDisplayName(true, true, false));
					if (Left.SelectedValueSeries != 0)
						s.Append(".Values[" + Left.SelectedValueSeries + "]");
					s.Append("[" + LeftBarsAgo + "].ApproxCompare(");
					s.Append(Left.IsOverlay ? (UsePriceToCompare ? "Close" : Right.GetDisplayName(true, true, false) + (Right.SelectedValueSeries != 0 ? ".Values[" + Right.SelectedValueSeries + "]" : string.Empty)) + "[" + RightBarsAgo + "]" : CompareValue.ToString(CultureInfo.InvariantCulture));
					s.Append(") <= 0");
					break;

				case Condition.NotEqual:
					s.Append(Left.GetDisplayName(true, true, false));
					if (Left.SelectedValueSeries != 0)
						s.Append(".Values[" + Left.SelectedValueSeries + "]");
					s.Append("[" + LeftBarsAgo + "].ApproxCompare(");
					s.Append(Left.IsOverlay ?(UsePriceToCompare ? "Close" :  Right.GetDisplayName(true, true, false) + (Right.SelectedValueSeries != 0 ? ".Values[" + Right.SelectedValueSeries + "]" : string.Empty)) + "[" + RightBarsAgo + "]" : CompareValue.ToString(CultureInfo.InvariantCulture));
					s.Append(") != 0");
					break;
			}
		}

		public void PrintAddChartIndicator(GeneratedStrategyLogic logic, StringBuilder s, int indentationLevel)
		{
			string text = "AddChartIndicator(" + Left.GetDisplayName(true, true, false) + ");" + Environment.NewLine;
			if (!logic.ChartIndicators.Contains(text))
			{
				logic.ChartIndicators.Add(text);
				s.Indent(indentationLevel);
				s.Append(text);
			}

			if (!UsePriceToCompare)
			{
				text = "AddChartIndicator(" + Right.GetDisplayName(true, true, false) + ");" + Environment.NewLine;
				if (!logic.ChartIndicators.Contains(text))
				{
					logic.ChartIndicators.Add(text);
					s.Indent(indentationLevel);
					s.Append(text);
				}
			}
		}

		public IndicatorBase Right
		{ get; set; }

		public int RightBarsAgo
		{ get; set; }

		private double RoundToNearestDecimal(double value, bool up)
		{
			if (value == double.MinValue || value == double.MinValue || double.IsNaN(value))
				return value;

			bool	isPositive	= value.ApproxCompare(0) >= 0;
			double	ret			= 0.0000000001 * (value.ApproxCompare(0) >= 0 ? 1 : -1);
			for (;; ret *= 10)
				for (int i = 1; i <= 10; i++)
					if ((up && isPositive && (ret * i).ApproxCompare(value) >= 0)
							|| (!up && isPositive && (ret * (i + 1)).ApproxCompare(value) >= 0)
							|| (up && !isPositive && (ret * (i + 1)).ApproxCompare(value) <= 0)
							|| (!up && !isPositive && (ret * i).ApproxCompare(value) <= 0))
						return (ret * i);
		}

		public XElement ToXml()
		{
			XElement ret = new XElement(GetType().Name);

			if (Left != null)
				using (StringWriter stringWriter = new StringWriter(CultureInfo.InvariantCulture))
				{
					new XmlSerializer(Left.GetType()).Serialize(stringWriter, Left);

					ret.Add(new XElement("LeftType", Left.GetType().FullName));
					ret.Add(new XElement("Left", XElement.Parse(stringWriter.ToString())));
				}

			if (Right != null)
				using (StringWriter stringWriter = new StringWriter(CultureInfo.InvariantCulture))
				{
					new XmlSerializer(Right.GetType()).Serialize(stringWriter, Right);

					ret.Add(new XElement("RightType", Right.GetType().FullName));
					ret.Add(new XElement("Right", XElement.Parse(stringWriter.ToString())));
				}

			ret.Add(new XElement("CompareFactor", CompareFactor.ToString(CultureInfo.InvariantCulture)));
			ret.Add(new XElement("Condition", Condition.ToString()));
			ret.Add(new XElement("LeftBarsAgo", LeftBarsAgo.ToString()));
			ret.Add(new XElement("MaxCompare", MaxCompare.ToString(CultureInfo.InvariantCulture)));
			ret.Add(new XElement("MinCompare", MinCompare.ToString(CultureInfo.InvariantCulture)));
			ret.Add(new XElement("RightBarsAgo", RightBarsAgo.ToString()));
			ret.Add(new XElement("UsePriceToCompare", UsePriceToCompare.ToString()));

			return ret;
		}

		public bool UsePriceToCompare
		{ get; set; }
	}

	internal class LogicalExpression : IExpression
	{
		private int r0 = -1;
		private int r1 = -1;

		public object Clone()
		{
			return new LogicalExpression { Left = (IExpression) Left.Clone(), Operator = Operator, Right = (IExpression) Right.Clone() };
		}

		public bool Evaluate(GeneratedStrategyLogic logic, StrategyBase strategy)
		{
			switch (Operator)
			{
				case LogicalOperator.And:	return Left.Evaluate(logic, strategy) && Right.Evaluate(logic, strategy);
				case LogicalOperator.Not:	return !Left.Evaluate(logic, strategy);
				case LogicalOperator.Or:	return Left.Evaluate(logic, strategy) || Right.Evaluate(logic, strategy);
				default:					return false;
			}
		}

		public static IExpression FromXml(XElement element)
		{
			return new LogicalExpression
						{
							Left		= element.Element("Left").Elements().First().Name == "IndicatorExpression" ? IndicatorExpression.FromXml(element.Element("Left").Elements().First()) 
											: (element.Element("Left").Elements().First().Name == "CandleStickPatternExpression" ? CandleStickPatternExpression.FromXml(element.Element("Left").Elements().First())  : FromXml(element.Element("Left").Elements().First())),
							Operator	= (LogicalOperator) Enum.Parse(typeof(LogicalOperator), element.Element("Operator").Value),
							Right		= element.Element("Right").Elements().First().Name == "IndicatorExpression" ? IndicatorExpression.FromXml(element.Element("Right").Elements().First()) 
											: (element.Element("Right").Elements().First().Name == "CandleStickPatternExpression" ? CandleStickPatternExpression.FromXml(element.Element("Right").Elements().First())  : FromXml(element.Element("Right").Elements().First())),
						};
		}

		public List<IExpression> GetExpressions()
		{
			List<IExpression> ret = new List<IExpression>();
			ret.Add(this);
			ret.AddRange(Left.GetExpressions());
			ret.AddRange(Right.GetExpressions());

			return ret;
		}

		public void Initialize(StrategyBase strategy)
		{
			Left.Initialize(strategy);
			Right.Initialize(strategy);
		}

		public IExpression Left
		{ get; set; }

		public IExpression NewMutation(GeneratedStrategyLogic logic, Random random, IExpression toMutate)
		{
			if (!logic.TryLinearMutation)
			{
				r0 = random.Next(10);
				r1 = random.Next(GeneratedStrategyLogic.NumLogicalOperators);
			}

			return new LogicalExpression {
					Operator	= (toMutate == this && r0 < 6				? (LogicalOperator) r1 : Operator),
					Left		= (toMutate == this && r0 >= 6 && r0 < 8	? logic.RandomExpression(random) : Left.NewMutation(logic, random, toMutate)),
					Right		= (toMutate == this && r0 >= 8				? logic.RandomExpression(random) : Right.NewMutation(logic, random, toMutate)) };
		}

		public LogicalOperator Operator
		{ get; set; }

		public void Print(StringBuilder s, int indentationLevel)
		{
			switch (Operator)
			{
				case LogicalOperator.And:
					s.Append('('); Left.Print(s, indentationLevel + 1); s.Append(Environment.NewLine);
						s.Indent(indentationLevel); s.Append("&& "); Right.Print(s, indentationLevel + 1); s.Append(')'); 
					break;
				case LogicalOperator.Not:
					s.Append("!("); Left.Print(s, indentationLevel + 1); s.Append(')'); 
					break;
				case LogicalOperator.Or:
					s.Append('('); Left.Print(s, indentationLevel + 1); s.Append(Environment.NewLine);
						s.Indent(indentationLevel); s.Append("|| "); Right.Print(s, indentationLevel + 1); s.Append(')'); 
					break;
				default:
					break;
			}
		}

		public void PrintAddChartIndicator(GeneratedStrategyLogic logic, StringBuilder stringBuilder, int indentationLevel)
		{
			Left.PrintAddChartIndicator(logic, stringBuilder, indentationLevel);
			Right.PrintAddChartIndicator(logic, stringBuilder, indentationLevel);
		}

		public IExpression Right
		{ get; set; }

		public XElement ToXml()
		{
			XElement ret = new XElement(GetType().Name);

			ret.Add(new XElement("Left", Left.ToXml()));
			ret.Add(new XElement("Operator", Operator.ToString()));
			ret.Add(new XElement("Right", Right.ToXml()));

			return ret;
		}
	}

	public sealed class GeneratedStrategyLogic : GeneratedStrategyLogicBase
	{
		private		Indicators.CandleStickPatternLogic	candleStickPatternLogic;
		private		static int							daysOfWeekCount				= Enum.GetValues(typeof(DayOfWeek)).Length;
		private		DateTime							endTimeForLongEntries;
		private		DateTime							endTimeForLongExits;
		private		DateTime							endTimeForShortEntries;
		private		DateTime							endTimeForShortExits;
		private		int									isInitialized;
		private		static long							lastId						= -1;
		private		int									minutesStep					= 15;
		private		int									numNodes					= -1;
		private		int									r0							= -1;
		private		int									r1							= -1;
		private		int									r2							= -1;
		private		int									r3							= -1;
		private		Data.SessionIterator				sessionIterator;
		private		DateTime							startTimeForLongEntries;
		private		DateTime							startTimeForLongExits;
		private		DateTime							startTimeForShortEntries;
		private		DateTime							startTimeForShortExits;
		private		double								stopTargetPercentStep		= 0.0025;		// intial stops are 0.25% 0.5% 0.75% 1% 1.25% 1.5% 1.75% or 2%, initial target is double that value
		private		static object						syncRoot					= new object();

		internal	static readonly int					NumConditions				= 7;
		internal	static readonly int					NumLogicalOperators			= Enum.GetValues(typeof(LogicalOperator)).Length;

		public List<string> ChartIndicators
		{ get; set; }

		/// <summary>
		/// Create a clone.
		/// </summary>
		/// <returns></returns>
		public override object Clone()
		{
			GeneratedStrategyLogic ret = new GeneratedStrategyLogic
								{
									EnterLongCondition					= (EnterLongCondition	== null ? null : (IExpression) EnterLongCondition.Clone()),
									EnterOnDayOfWeek					= EnterOnDayOfWeek,
									EnterShortCondition					= (EnterShortCondition	== null ? null : (IExpression) EnterShortCondition.Clone()),
									ExitLongCondition					= (ExitLongCondition	== null ? null : (IExpression) ExitLongCondition.Clone()),
									ExitShortCondition					= (ExitShortCondition	== null ? null : (IExpression) ExitShortCondition.Clone()),
									ExitOnDayOfWeek						= ExitOnDayOfWeek,
									ExitOnSessionClose					= ExitOnSessionClose,
									Id									= Id,
									ParabolicStopPercent				= ParabolicStopPercent,
									ProfitTargetPercent					= ProfitTargetPercent,
									r0									= r0,
									r1									= r1,
									r2									= r2,
									r3									= r3,
									SessionMinutesForLongEntries		= SessionMinutesForLongEntries,
									SessionMinutesForLongExits			= SessionMinutesForLongExits,
									SessionMinutesForShortEntries		= SessionMinutesForShortEntries,
									SessionMinutesForShortExits			= SessionMinutesForShortExits,
									SessionMinutesOffsetForLongEntries	= SessionMinutesOffsetForLongEntries,
									SessionMinutesOffsetForLongExits	= SessionMinutesOffsetForLongExits,
									SessionMinutesOffsetForShortEntries	= SessionMinutesOffsetForShortEntries,
									SessionMinutesOffsetForShortExits	= SessionMinutesOffsetForShortExits,
									StopLossPercent						= StopLossPercent,
									TrailStopPercent					= TrailStopPercent,
									TrendStrength						= TrendStrength,
									TryLinearMutation					= TryLinearMutation,
									StrategyGenerator					= StrategyGenerator,
								};

			return ret;
		}

		public bool[] EnterOnDayOfWeek
		{ get; set; }

		internal IExpression EnterLongCondition
		{ get; set; }

		internal IExpression EnterShortCondition
		{ get; set; }

		internal IExpression ExitLongCondition
		{ get; set; }

		public bool[] ExitOnDayOfWeek
		{ get; set; }

		internal IExpression ExitShortCondition
		{ get; set; }

		internal bool? ExitOnSessionClose
		{ get; set; }

		/// <summary>
		/// Populate an instance from XML
		/// </summary>
		/// <param name="element"></param>
		public override void FromXml(XElement element)
		{
			EnterLongCondition					= element.Element("EnterLongCondition")		!= null ? (element.Element("EnterLongCondition").Elements().First().Name	== "IndicatorExpression" ? IndicatorExpression.FromXml(element.Element("EnterLongCondition").Elements().First())	
													: (element.Element("EnterLongCondition").Elements().First().Name	== "CandleStickPatternExpression" ? CandleStickPatternExpression.FromXml(element.Element("EnterLongCondition").Elements().First()) : LogicalExpression.FromXml(element.Element("EnterLongCondition").Elements().First())))		: null;
			EnterShortCondition					= element.Element("EnterShortCondition")	!= null ? (element.Element("EnterShortCondition").Elements().First().Name	== "IndicatorExpression" ? IndicatorExpression.FromXml(element.Element("EnterShortCondition").Elements().First())
													: (element.Element("EnterShortCondition").Elements().First().Name	== "CandleStickPatternExpression" ? CandleStickPatternExpression.FromXml(element.Element("EnterShortCondition").Elements().First()) : LogicalExpression.FromXml(element.Element("EnterShortCondition").Elements().First())))	: null;
			ExitLongCondition					= element.Element("ExitLongCondition")		!= null ? (element.Element("ExitLongCondition").Elements().First().Name		== "IndicatorExpression" ? IndicatorExpression.FromXml(element.Element("ExitLongCondition").Elements().First())
													: (element.Element("ExitLongCondition").Elements().First().Name		== "CandleStickPatternExpression" ? CandleStickPatternExpression.FromXml(element.Element("ExitLongCondition").Elements().First()) : LogicalExpression.FromXml(element.Element("ExitLongCondition").Elements().First())))		: null;
			ExitShortCondition					= element.Element("ExitShortCondition")		!= null ? (element.Element("ExitShortCondition").Elements().First().Name	== "IndicatorExpression" ? IndicatorExpression.FromXml(element.Element("ExitShortCondition").Elements().First())
													: (element.Element("ExitShortCondition").Elements().First().Name	== "CandleStickPatternExpression" ? CandleStickPatternExpression.FromXml(element.Element("ExitShortCondition").Elements().First()) : LogicalExpression.FromXml(element.Element("ExitShortCondition").Elements().First())))		: null;

			if (element.Element("EnterOnDayOfWeek") != null)
			{
				EnterOnDayOfWeek				= new bool[daysOfWeekCount];
				for (int i = 0; i < element.Element("EnterOnDayOfWeek").Value.Length; i++)
					EnterOnDayOfWeek[i]			= (element.Element("EnterOnDayOfWeek").Value[i] == '1');
			}

			if (element.Element("ExitOnDayOfWeek") != null)
			{
				ExitOnDayOfWeek					= new bool[daysOfWeekCount];
				for (int i = 0; i < element.Element("ExitOnDayOfWeek").Value.Length; i++)
					ExitOnDayOfWeek[i]			= (element.Element("ExitOnDayOfWeek").Value[i] == '1');
			}

			if (element.Element("ExitOnSessionClose") != null)
				ExitOnSessionClose				= bool.Parse(element.Element("ExitOnSessionClose").Value);

			ParabolicStopPercent				= double.Parse(element.Element("ParabolicStopPercent").Value, CultureInfo.InvariantCulture);
			ProfitTargetPercent					= double.Parse(element.Element("ProfitTargetPercent").Value, CultureInfo.InvariantCulture);
			SessionMinutesForLongEntries		= int.Parse(element.Element("SessionMinutesForLongEntries").Value, CultureInfo.InvariantCulture);
			SessionMinutesForLongExits			= int.Parse(element.Element("SessionMinutesForLongExits").Value, CultureInfo.InvariantCulture);
			SessionMinutesForShortEntries		= int.Parse(element.Element("SessionMinutesForShortEntries").Value, CultureInfo.InvariantCulture);
			SessionMinutesForShortExits			= int.Parse(element.Element("SessionMinutesForShortExits").Value, CultureInfo.InvariantCulture);
			SessionMinutesOffsetForLongEntries	= int.Parse(element.Element("SessionMinutesOffsetForLongEntries").Value, CultureInfo.InvariantCulture);
			SessionMinutesOffsetForLongExits	= int.Parse(element.Element("SessionMinutesOffsetForLongExits").Value, CultureInfo.InvariantCulture);
			SessionMinutesOffsetForShortEntries	= int.Parse(element.Element("SessionMinutesOffsetForShortEntries").Value, CultureInfo.InvariantCulture);
			SessionMinutesOffsetForShortExits	= int.Parse(element.Element("SessionMinutesOffsetForShortExits").Value, CultureInfo.InvariantCulture);
			StopLossPercent						= double.Parse(element.Element("StopLossPercent").Value, CultureInfo.InvariantCulture);
			TrailStopPercent					= double.Parse(element.Element("TrailStopPercent").Value, CultureInfo.InvariantCulture);
			TrendStrength						= int.Parse(element.Element("TrendStrength").Value, CultureInfo.InvariantCulture);
		}

		public Indicators.CandleStickPatternLogic GetCandleStickPatternLogic(StrategyBase strategy)
		{
			if (candleStickPatternLogic != null)
				return candleStickPatternLogic;

			lock (syncRoot)
				if (candleStickPatternLogic == null)
					candleStickPatternLogic = new Indicators.CandleStickPatternLogic(strategy, TrendStrength);

			return candleStickPatternLogic;
		}

		public bool HasCandleStickPatternExpression
		{
			get
			{
				return ((EnterLongCondition != null && EnterLongCondition.GetExpressions().FirstOrDefault(e => e is CandleStickPatternExpression) != null)
					|| (ExitLongCondition != null && ExitLongCondition.GetExpressions().FirstOrDefault(e => e is CandleStickPatternExpression) != null)
					|| (EnterShortCondition != null && EnterShortCondition.GetExpressions().FirstOrDefault(e => e is CandleStickPatternExpression) != null)
					|| (ExitShortCondition != null && ExitShortCondition.GetExpressions().FirstOrDefault(e => e is CandleStickPatternExpression) != null));
			}
		}

		public long Id
		{ get; private set; }

		public bool IsConsistent
		{
			get
			{ 
				return ((double.IsNaN(StopLossPercent) && double.IsNaN(TrailStopPercent)) || !double.IsNaN(ProfitTargetPercent))
							&& ((double.IsNaN(ParabolicStopPercent) && ExitShortCondition == null) || double.IsNaN(ProfitTargetPercent))
							&& ((SessionMinutesForLongEntries == -1 && SessionMinutesOffsetForLongEntries == -1) || (SessionMinutesForLongEntries >= -1 && SessionMinutesOffsetForLongEntries >= -1))
							&& ((SessionMinutesForLongExits == -1 && SessionMinutesOffsetForLongExits == -1) || (SessionMinutesForLongExits >= -1 && SessionMinutesOffsetForLongExits >= -1))
							&& ((SessionMinutesForShortEntries == -1 && SessionMinutesOffsetForShortEntries == -1) || (SessionMinutesForShortEntries >= -1 && SessionMinutesOffsetForShortEntries >= -1))
							&& ((SessionMinutesForShortExits == -1 && SessionMinutesOffsetForShortExits == -1) || (SessionMinutesForShortExits >= -1 && SessionMinutesOffsetForShortExits >= -1))
							&& TrendStrength > 0;
			}
		}

		public bool IsLong
		{
			get { return EnterLongCondition != null || SessionMinutesOffsetForLongEntries >= 0; }
		}

		public bool IsShort
		{
			get { return EnterShortCondition != null || SessionMinutesOffsetForShortEntries >= 0; }
		}

		internal GeneratedStrategyLogic NewCrossOver(GeneratedStrategyLogic fitter, Random random)
		{
			// 0: entry
			// 1: exit
			// 2: trend strength
			int			r	= random.Next(3);
			GeneratedStrategyLogic	ret = new GeneratedStrategyLogic
							{
								EnterLongCondition					= (r == 0 ? (fitter.EnterLongCondition	!= null			? fitter.EnterLongCondition.Clone() as IExpression	: null)	: (EnterLongCondition	!= null ? EnterLongCondition.Clone() as IExpression		: null)),
								EnterShortCondition					= (r == 0 ? (fitter.EnterShortCondition	!= null			? fitter.EnterShortCondition.Clone() as IExpression : null)	: (EnterShortCondition	!= null ? EnterShortCondition.Clone() as IExpression	: null)),
								EnterOnDayOfWeek					= (r == 0 ? fitter.EnterOnDayOfWeek						: EnterOnDayOfWeek),
								ExitLongCondition					= (r == 1 ? (fitter.ExitLongCondition	!= null			? fitter.ExitLongCondition.Clone() as IExpression	: null)	: (ExitLongCondition	!= null ? ExitLongCondition.Clone() as IExpression		: null)),
								ExitShortCondition					= (r == 1 ? (fitter.ExitShortCondition	!= null			? fitter.ExitShortCondition.Clone() as IExpression	: null)	: (ExitShortCondition	!= null ? ExitShortCondition.Clone() as IExpression		: null)),
								ExitOnDayOfWeek						= (r == 1 ? fitter.ExitOnDayOfWeek						: ExitOnDayOfWeek),
								ExitOnSessionClose					= (r == 1 ? fitter.ExitOnSessionClose					: ExitOnSessionClose),
								ParabolicStopPercent				= (r == 1 ? fitter.ParabolicStopPercent					: ParabolicStopPercent),
								ProfitTargetPercent					= (r == 1 ? fitter.ProfitTargetPercent					: ProfitTargetPercent),
								SessionMinutesForLongEntries		= (r == 0 ? fitter.SessionMinutesForLongEntries			: SessionMinutesForLongEntries),
								SessionMinutesForLongExits			= (r == 1 ? fitter.SessionMinutesForLongExits			: SessionMinutesForLongExits),
								SessionMinutesForShortEntries		= (r == 0 ? fitter.SessionMinutesForShortEntries		: SessionMinutesForShortEntries),
								SessionMinutesForShortExits			= (r == 1 ? fitter.SessionMinutesForShortExits			: SessionMinutesForShortExits),
								SessionMinutesOffsetForLongEntries	= (r == 0 ? fitter.SessionMinutesOffsetForLongEntries	: SessionMinutesOffsetForLongEntries),
								SessionMinutesOffsetForLongExits	= (r == 1 ? fitter.SessionMinutesOffsetForLongExits		: SessionMinutesOffsetForLongExits),
								SessionMinutesOffsetForShortEntries	= (r == 0 ? fitter.SessionMinutesOffsetForShortEntries	: SessionMinutesOffsetForShortEntries),
								SessionMinutesOffsetForShortExits	= (r == 1 ? fitter.SessionMinutesOffsetForShortExits	: SessionMinutesOffsetForShortExits),
								StopLossPercent						= (r == 1 ? fitter.StopLossPercent						: StopLossPercent),
								TrailStopPercent					= (r == 1 ? fitter.TrailStopPercent						: TrailStopPercent),
								TrendStrength						= (r == 2 ? fitter.TrendStrength						: TrendStrength),
								StrategyGenerator					= StrategyGenerator,
							};

			if (!ret.IsConsistent)
				throw new InvalidOperationException("NewCrossOver");

			return ret;
		}

		internal GeneratedStrategyLogic NewMutation(Random random)
		{
			if (!TryLinearMutation)
			{
				// dependent on the actual entry/exit conditions enabled, this might not create a new mutation.
				// however, this is irrelevant, since the caller tries N times
				//
				// 0: entry
				// 1: exit
				// 2: trend strength
				// 3: exit on session close
				// 4: enter on day of week
				// 5: exit on day of week
				// 6: session time
				r0								= random.Next(7);

				// 0: long
				// 1: short
				r1								= random.Next(2);

				// 0: increment
				// 1: decrement
				r2								= random.Next(2);
			}

			List<IExpression>		expressions;
			GeneratedStrategyLogic				ret			= new GeneratedStrategyLogic { PriorPerformance = PriorPerformance, TryLinearMutation = TryLinearMutation, StrategyGenerator = StrategyGenerator };

			if (r0 == 0 && r1 == 0 && EnterLongCondition != null)
			{
				expressions						= EnterLongCondition.GetExpressions();
				if (!ret.TryLinearMutation)
					r3							= random.Next(expressions.Count);
				ret.EnterLongCondition			= EnterLongCondition.NewMutation(ret, random, expressions[r3]);
			}
			else
				ret.EnterLongCondition			= (EnterLongCondition != null ? EnterLongCondition.Clone() as IExpression : null);

			if (r0 == 0 && r1 == 1 && EnterShortCondition != null)
			{
				expressions						= EnterShortCondition.GetExpressions();
				if (!ret.TryLinearMutation)
					r3							= random.Next(expressions.Count);
				ret.EnterShortCondition			= EnterShortCondition.NewMutation(ret, random, expressions[r3]);
			}
			else
				ret.EnterShortCondition			= (EnterShortCondition != null ? EnterShortCondition.Clone() as IExpression : null);

			if (r0 == 1 && r1 == 0 && ExitLongCondition != null)
			{
				expressions						= ExitLongCondition.GetExpressions();
				if (!ret.TryLinearMutation)
					r3							= random.Next(expressions.Count);
				ret.ExitLongCondition			= ExitLongCondition.NewMutation(ret, random, expressions[r3]);
			}
			else
				ret.ExitLongCondition			= (ExitLongCondition != null ? ExitLongCondition.Clone() as IExpression : null);

			if (r0 == 1 && r1 == 1 && ExitShortCondition != null)
			{
				expressions						= ExitShortCondition.GetExpressions();
				if (!ret.TryLinearMutation)
					r3							= random.Next(expressions.Count);
				ret.ExitShortCondition			= ExitShortCondition.NewMutation(ret, random, expressions[r3]);
			}
			else
				ret.ExitShortCondition			= (ExitShortCondition != null ? ExitShortCondition.Clone() as IExpression : null);

			if (r0 == 1 && !double.IsNaN(ParabolicStopPercent))
			{
				ret.TryLinearMutation			= true;
				ret.ParabolicStopPercent		= Math.Max(stopTargetPercentStep, ParabolicStopPercent + (r2 == 0 ? 1 : -1) * stopTargetPercentStep);
			}
			else
				ret.ParabolicStopPercent		= ParabolicStopPercent;

			if (r0 == 1 && !double.IsNaN(ProfitTargetPercent))
			{
				ret.TryLinearMutation			= true;
				ret.ProfitTargetPercent			= Math.Max(stopTargetPercentStep, ProfitTargetPercent + (r2 == 0 ? 1 : -1) * stopTargetPercentStep);
			}
			else
				ret.ProfitTargetPercent			= ProfitTargetPercent;

			if (r0 == 1 && !double.IsNaN(StopLossPercent))
			{
				ret.TryLinearMutation			= true;
				ret.StopLossPercent				= Math.Max(stopTargetPercentStep, StopLossPercent + (r2 == 0 ? 1 : -1) * stopTargetPercentStep);
			}
			else
				ret.StopLossPercent				= StopLossPercent;

			if (r0 == 1 && !double.IsNaN(TrailStopPercent))
			{
				ret.TryLinearMutation			= true;
				ret.TrailStopPercent			= Math.Max(stopTargetPercentStep, TrailStopPercent + (r2 == 0 ? 1 : -1) * stopTargetPercentStep);
			}
			else
				ret.TrailStopPercent			= TrailStopPercent;

			if (r0 == 2)
			{
				ret.TryLinearMutation			= true;
				ret.TrendStrength				= Math.Max(2, 2 + (r2 == 0 ? 1 : -1));
			}
			else
				ret.TrendStrength				= TrendStrength;

			if (r0 == 3 && StrategyGenerator.UseSessionCloseForExits)
				ret.ExitOnSessionClose			= !ExitOnSessionClose;
			else
				ret.ExitOnSessionClose			= ExitOnSessionClose;

			if (r0 == 4 && StrategyGenerator.UseDayOfWeekForEntries)
			{
				int r							= random.Next(daysOfWeekCount);
				ret.EnterOnDayOfWeek			= EnterOnDayOfWeek.ToArray();
				ret.EnterOnDayOfWeek[r]			= !EnterOnDayOfWeek[r];
			}
			else
				ret.EnterOnDayOfWeek			= EnterOnDayOfWeek;

			if (r0 == 4 && StrategyGenerator.UseDayOfWeekForExits)
			{
				int r							= random.Next(daysOfWeekCount);
				ret.ExitOnDayOfWeek				= ExitOnDayOfWeek.ToArray();
				ret.ExitOnDayOfWeek[r]			= !ExitOnDayOfWeek[r];
			}
			else
				ret.ExitOnDayOfWeek				= ExitOnDayOfWeek;

			if (r0 == 6)
			{
				if (!ret.TryLinearMutation)
					r3							= random.Next(4);

				ret.TryLinearMutation			= true;
				switch (r3)
				{
					case 0:		ret.SessionMinutesForLongEntries		= (SessionMinutesForLongEntries			== -1 ? -1 : Math.Max(1, Math.Min(9 * minutesStep, minutesStep + (r2 == 0 ? 1 : -1) * minutesStep)));
								ret.SessionMinutesForShortEntries		= (SessionMinutesForShortEntries		== -1 ? -1 : Math.Max(1, Math.Min(9 * minutesStep, minutesStep + (r2 == 0 ? 1 : -1) * minutesStep)));
								break;
					case 1:		ret.SessionMinutesForLongExits			= (SessionMinutesForLongExits			== -1 ? -1 : Math.Max(1, Math.Min(9 * minutesStep, minutesStep + (r2 == 0 ? 1 : -1) * minutesStep)));
								ret.SessionMinutesForShortExits			= (SessionMinutesForShortExits			== -1 ? -1 : Math.Max(1, Math.Min(9 * minutesStep, minutesStep + (r2 == 0 ? 1 : -1) * minutesStep)));
								break;
					case 2:		ret.SessionMinutesOffsetForLongEntries	= (SessionMinutesOffsetForLongEntries	== -1 ? -1 : Math.Max(0, Math.Min(5 * minutesStep, minutesStep + (r2 == 0 ? 1 : -1) * minutesStep)));
								ret.SessionMinutesOffsetForShortEntries	= (SessionMinutesOffsetForShortEntries	== -1 ? -1 : Math.Max(0, Math.Min(5 * minutesStep, minutesStep + (r2 == 0 ? 1 : -1) * minutesStep)));
								break;
					case 3:		ret.SessionMinutesOffsetForLongExits	= (SessionMinutesOffsetForLongExits		== -1 ? -1 : Math.Max(0, Math.Min(5 * minutesStep, minutesStep + (r2 == 0 ? 1 : -1) * minutesStep)));
								ret.SessionMinutesOffsetForShortExits	= (SessionMinutesOffsetForShortExits	== -1 ? -1 : Math.Max(0, Math.Min(5 * minutesStep, minutesStep + (r2 == 0 ? 1 : -1) * minutesStep)));
								break;
				}
			}
			else
			{
				ret.SessionMinutesForLongEntries		= SessionMinutesForLongEntries;
				ret.SessionMinutesForLongExits			= SessionMinutesForLongExits;
				ret.SessionMinutesForShortEntries		= SessionMinutesForShortEntries;
				ret.SessionMinutesForShortExits			= SessionMinutesForShortExits;
				ret.SessionMinutesOffsetForLongEntries	= SessionMinutesOffsetForLongEntries;
				ret.SessionMinutesOffsetForLongExits	= SessionMinutesOffsetForLongExits;
				ret.SessionMinutesOffsetForShortEntries	= SessionMinutesOffsetForShortEntries;
				ret.SessionMinutesOffsetForShortExits	= SessionMinutesOffsetForShortExits;
			}

			if (!ret.IsConsistent)
				throw new InvalidOperationException("NewMutation");

			return ret;
		}

		internal GeneratedStrategyLogic NewRandom(Random random)
		{
			// 0: long and short
			// 1: long only
			// 2: short only
			int			r							= (StrategyGenerator.OptimizeEntries ? random.Next(3) : -1);

			// Exits are a bit tricky. We don't want to mix different forms of exists but keep them separate. Feel free to try a different logic as you see need and fit...
			// 0: Exit by 'condition' (indicator or candle stick, no other stop/target set)
			// 1: ParabolicStop (no ProfitTarget set)
			// 2: StopLoss + ProfitTarget
			// 3: TrailStop + ProfitTarget
			int			r2							= (!StrategyGenerator.OptimizeExits
															|| (!StrategyGenerator.UseCandleStickPatternForExits
																&& !StrategyGenerator.UseIndicatorsForExits
																&& !StrategyGenerator.UseParabolicStopForExits
																&& !StrategyGenerator.UseStopTargetsForExits)
														? -1 
														: random.Next((StrategyGenerator.UseCandleStickPatternForExits || StrategyGenerator.UseIndicatorsForExits ? 1 : 0)
																	+ (StrategyGenerator.UseParabolicStopForExits ? 1 : 0)
																	+ (StrategyGenerator.UseStopTargetsForExits ? 2 : 0)));
			
			if (r2 >= 0 && !StrategyGenerator.UseCandleStickPatternForExits && !StrategyGenerator.UseIndicatorsForExits)		r2 += 1;
			if (r2 >= 1 && !StrategyGenerator.UseParabolicStopForExits)														r2 += 1;
			
			// 0: no session time for entries
			// 1: session time for entries
			int			r3							= (StrategyGenerator.UseSessionTimeForEntries ? random.Next(2) : 0);

			// 0: no session time for exits
			// 1: session time for exits
			int			r4							= (StrategyGenerator.UseSessionTimeForExits ? random.Next(2) : 0);

			double		initialStopTargetPercent	= stopTargetPercentStep * (1 + random.Next(8));

			GeneratedStrategyLogic	ret							= new GeneratedStrategyLogic
						{
							EnterLongCondition					= ((r == 0 || r == 1)								? RandomExpression(random, true)		: null),
							EnterShortCondition					= ((r == 0 || r == 2)								? RandomExpression(random, true)		: null),
							EnterOnDayOfWeek					= (StrategyGenerator.UseDayOfWeekForEntries			? new bool[daysOfWeekCount]				: null),
							ExitLongCondition					= ((r == 0 || r == 1) && r2 == 0					? RandomExpression(random, false)		: null),
							ExitShortCondition					= ((r == 0 || r == 2) && r2 == 0					? RandomExpression(random, false)		: null),
							ExitOnDayOfWeek						= (StrategyGenerator.UseDayOfWeekForExits			? new bool[daysOfWeekCount]				: null),
							ExitOnSessionClose					= (StrategyGenerator.UseSessionCloseForExits		? new bool?(random.Next(2) == 0)		: new bool?()),
							ParabolicStopPercent				= (r2 == 1											? initialStopTargetPercent				: double.NaN),
							ProfitTargetPercent					= (r2 != 0 && r2 != 1								? 2 * initialStopTargetPercent			: double.NaN),
							SessionMinutesForLongEntries		= ((r == 0 || r == 1) && r3 == 1					? minutesStep * (1 + random.Next(8))	: -1),
							SessionMinutesForLongExits			= ((r == 0 || r == 1) && r4 == 1					? minutesStep * (1 + random.Next(8))	: -1),
							SessionMinutesForShortEntries		= ((r == 0 || r == 2) && r3 == 1					? minutesStep * (1 + random.Next(8))	: -1),
							SessionMinutesForShortExits			= ((r == 0 || r == 2) && r4 == 1					? minutesStep * (1 + random.Next(8))	: -1),
							SessionMinutesOffsetForLongEntries	= ((r == 0 || r == 1) && r3 == 1					? minutesStep * random.Next(5)			: -1),
							SessionMinutesOffsetForLongExits	= ((r == 0 || r == 1) && r4 == 1					? minutesStep * random.Next(5)			: -1),
							SessionMinutesOffsetForShortEntries	= ((r == 0 || r == 2) && r3 == 1					? minutesStep * random.Next(5)			: -1),
							SessionMinutesOffsetForShortExits	= ((r == 0 || r == 2) && r4 == 1					? minutesStep * random.Next(5)			: -1),
							StopLossPercent						= (r2 == 2											? initialStopTargetPercent				: double.NaN),
							TrailStopPercent					= (r2 == 3											? initialStopTargetPercent				: double.NaN),
							TrendStrength						= 2 + random.Next(9),								// mutate between 2 and 10)
							StrategyGenerator					= StrategyGenerator,
						};

			if (ret.EnterOnDayOfWeek != null)
				for (int i = 1; i < ret.EnterOnDayOfWeek.Length; i++)
					ret.EnterOnDayOfWeek[i] = (random.Next(2) == 0);

			if (ret.ExitOnDayOfWeek != null)
				for (int i = 1; i < ret.ExitOnDayOfWeek.Length; i++)
					ret.ExitOnDayOfWeek[i] = (random.Next(2) == 0);

			if (!ret.IsConsistent)
				throw new InvalidOperationException("NewRandom");

			return ret;
		}

		internal int NumNodes
		{
			get
			{
				if (numNodes >= 0)
					return numNodes;

				lock (syncRoot)
				{
					if (numNodes >= 0)
						return numNodes;

					numNodes = 0;
					numNodes += (EnterLongCondition		!= null ? EnterLongCondition.GetExpressions().Count		: 0);
					numNodes += (EnterShortCondition	!= null ? EnterShortCondition.GetExpressions().Count	: 0);
					numNodes += (ExitLongCondition		!= null ? ExitLongCondition.GetExpressions().Count		: 0);
					numNodes += (ExitShortCondition		!= null ? ExitShortCondition.GetExpressions().Count		: 0);

					numNodes += (!double.IsNaN(ParabolicStopPercent)			? 1 : 0);
					numNodes += (!double.IsNaN(ProfitTargetPercent)				? 1 : 0);
					numNodes += (!double.IsNaN(StopLossPercent)					? 1 : 0);
					numNodes += (!double.IsNaN(TrailStopPercent)				? 1 : 0);

					numNodes += (SessionMinutesOffsetForLongEntries		>= 0	? 1 : 0);
					numNodes += (SessionMinutesOffsetForLongExits		>= 0	? 1 : 0);
					numNodes += (SessionMinutesOffsetForShortEntries	>= 0	? 1 : 0);
					numNodes += (SessionMinutesOffsetForShortExits		>= 0	? 1 : 0);

					return numNodes;
				}
			}
		}

		/// <summary>
		/// Called on every OnBarUpdate. Implement your custom logic here.
		/// </summary>
		/// <param name="strategy"></param>
		public override void OnBarUpdate(StrategyBase strategy)
		{
			if (strategy.CurrentBars[0] < strategy.BarsRequiredToTrade)
				return;

			if (Interlocked.CompareExchange(ref isInitialized, 1, 0) == 0)
			{
				if (EnterLongCondition	!= null)	EnterLongCondition.Initialize(strategy);
				if (EnterShortCondition	!= null)	EnterShortCondition.Initialize(strategy);
				if (ExitLongCondition	!= null)	ExitLongCondition.Initialize(strategy);
				if (ExitShortCondition	!= null)	ExitShortCondition.Initialize(strategy);

				if (!double.IsNaN(ParabolicStopPercent))
					strategy.SetParabolicStop(CalculationMode.Percent, ParabolicStopPercent);

				if (!double.IsNaN(ProfitTargetPercent))
					strategy.SetProfitTarget(CalculationMode.Percent, ProfitTargetPercent);

				if (!double.IsNaN(StopLossPercent))
					strategy.SetStopLoss(CalculationMode.Percent, StopLossPercent);

				if (!double.IsNaN(TrailStopPercent))
					strategy.SetTrailStop(CalculationMode.Percent, TrailStopPercent);
			}

			if (sessionIterator == null && (SessionMinutesOffsetForLongEntries >= 0 || SessionMinutesOffsetForShortEntries >= 0 || SessionMinutesOffsetForLongExits >= 0 || SessionMinutesOffsetForShortExits >= 0)
				|| (sessionIterator != null && strategy.BarsArray[0].IsFirstBarOfSession))
			{
				if (sessionIterator == null)
				{
					sessionIterator = new Data.SessionIterator(strategy.BarsArray[0]);
					sessionIterator.GetNextSession(strategy.Times[0][0], true);
				}
				else if (strategy.BarsArray[0].IsFirstBarOfSession)
					sessionIterator.GetNextSession(strategy.Times[0][0], true);

				if (SessionMinutesOffsetForLongEntries >= 0)
				{
					startTimeForLongEntries		= sessionIterator.ActualSessionBegin.AddMinutes(SessionMinutesOffsetForLongEntries);
					endTimeForLongEntries		= startTimeForLongEntries.AddMinutes(SessionMinutesForLongEntries);
				}

				if (SessionMinutesOffsetForShortEntries >= 0)
				{
					startTimeForShortEntries	= sessionIterator.ActualSessionBegin.AddMinutes(SessionMinutesOffsetForShortEntries);
					endTimeForShortEntries		= startTimeForShortEntries.AddMinutes(SessionMinutesForShortEntries);
				}

				if (SessionMinutesOffsetForLongExits >= 0)
				{
					startTimeForLongExits		= sessionIterator.ActualSessionEnd.AddMinutes(-(SessionMinutesOffsetForLongExits + SessionMinutesForLongExits));
					endTimeForLongExits			= startTimeForLongExits.AddMinutes(SessionMinutesForLongExits);
				}

				if (SessionMinutesOffsetForShortExits >= 0)
				{
					startTimeForShortExits		= sessionIterator.ActualSessionEnd.AddMinutes(-(SessionMinutesOffsetForShortExits + SessionMinutesForShortExits));
					endTimeForShortExits		= startTimeForShortExits.AddMinutes(SessionMinutesForShortExits);
				}
			}

			Cbi.Order order;
			if ((EnterLongCondition != null || SessionMinutesOffsetForLongEntries >= 0)
					&& (EnterLongCondition == null || EnterLongCondition.Evaluate(this, strategy) == true)
					&& (SessionMinutesOffsetForLongEntries == -1 || (startTimeForLongEntries < strategy.Times[0][0] && strategy.Times[0][0] <= endTimeForLongEntries))
					&& (EnterOnDayOfWeek == null || EnterOnDayOfWeek[(int) strategy.Times[0][0].DayOfWeek] == true))
				order = (strategy is IGeneratedStrategy ? (strategy as IGeneratedStrategy).OnEnterLong() : strategy.EnterLong());
			
			if ((ExitLongCondition != null || SessionMinutesOffsetForLongExits >= 0)
					&& (ExitLongCondition == null || ExitLongCondition.Evaluate(this, strategy) == true)
					&& (SessionMinutesOffsetForLongExits == -1 || (startTimeForLongExits < strategy.Times[0][0] && strategy.Times[0][0] <= endTimeForLongExits))
					&& (ExitOnDayOfWeek == null || ExitOnDayOfWeek[(int) strategy.Times[0][0].DayOfWeek] == true))
				order = (strategy is IGeneratedStrategy ? (strategy as IGeneratedStrategy).OnExitLong() : strategy.ExitLong());

			if ((EnterShortCondition != null || SessionMinutesOffsetForShortEntries >= 0)
					&& (EnterShortCondition == null || EnterShortCondition.Evaluate(this, strategy) == true)
					&& (SessionMinutesOffsetForShortEntries == -1 || (startTimeForShortEntries < strategy.Times[0][0] && strategy.Times[0][0] <= endTimeForShortEntries))
					&& (EnterOnDayOfWeek == null || EnterOnDayOfWeek[(int) strategy.Times[0][0].DayOfWeek] == true))
				order = (strategy is IGeneratedStrategy ? (strategy as IGeneratedStrategy).OnEnterShort() : strategy.EnterShort());
			
			if ((ExitShortCondition != null || SessionMinutesOffsetForShortExits >= 0)
					&& (ExitShortCondition == null || ExitShortCondition.Evaluate(this, strategy) == true)
					&& (SessionMinutesOffsetForShortExits == -1 || (startTimeForShortExits < strategy.Times[0][0] && strategy.Times[0][0] <= endTimeForShortExits))
					&& (ExitOnDayOfWeek == null || ExitOnDayOfWeek[(int) strategy.Times[0][0].DayOfWeek] == true))
				order = (strategy is IGeneratedStrategy ? (strategy as IGeneratedStrategy).OnExitShort() : strategy.ExitShort());
		}

		/// <summary>
		/// Called on every OnStateChange. Implement your custom logic here.
		/// </summary>
		/// <param name="strategy"></param>
		public override void OnStateChange(StrategyBase strategy)
		{
			if (strategy.State == State.Configure && ExitOnSessionClose.HasValue)
				strategy.IsExitOnSessionCloseStrategy = ExitOnSessionClose.Value;
		}

		public double ParabolicStopPercent
		{ get; set; }

		public double PriorPerformance
		{ get; set; }

		public double ProfitTargetPercent
		{ get; set; }

		internal IExpression RandomExpression(Random random, bool? isEntry = null)
		{
			bool useCandleStickPattern	= StrategyGenerator.SelectedCandleStickPattern.Length > 0
											&& (isEntry == null || (isEntry == true && StrategyGenerator.UseCandleStickPatternForEntries)	|| (isEntry == false && StrategyGenerator.UseCandleStickPatternForExits));
			bool useIndicators			= StrategyGenerator.SelectedIndicatorTypes.Length > 0
											&& (isEntry == null || (isEntry == true && StrategyGenerator.UseIndicatorsForEntries)			|| (isEntry == false && StrategyGenerator.UseIndicatorsForExits));

			int r = random.Next(1 + (useCandleStickPattern ? 2 : 0) + (useIndicators ? 2 : 0));

			if (!useCandleStickPattern && !useIndicators)
				return null;
			else if (r == 0)
				return new LogicalExpression { Left = RandomExpression(random, isEntry), Operator = (LogicalOperator) random.Next(NumLogicalOperators), Right = RandomExpression(random, isEntry) };
			else if (useCandleStickPattern && r <= 2)
				return new CandleStickPatternExpression { Pattern = RandomCandleStickPattern(random) };
			else
			{
				IndicatorExpression ret = new IndicatorExpression {
													CompareFactor		= random.Next(101) / 100.0,
													Condition			= (Condition) random.Next(NumConditions),
													Left				= RandomIndicator(random),
													LeftBarsAgo			= 0,
													Right				= RandomIndicator(random),		// Not needed in all cases. However, it makes our lives easier if it's set
													RightBarsAgo		= 0,
													UsePriceToCompare	= (random.Next(2) == 0) };

				Tuple<double, double> minMax;
				if (Optimizers.StrategyGenerator.AvailableIndicators.TryGetValue(ret.Left.GetType(), out minMax) && minMax != null)
				{
					ret.MinCompare = minMax.Item1;
					ret.MaxCompare = minMax.Item2;
				}

				if (ret.Left.IsOverlay)
					while (!ret.Right.IsOverlay)
					{
						try { ret.Right.SetState(NinjaTrader.NinjaScript.State.Finalized); } catch {}
						ret.Right = RandomIndicator(random);
					}
				else
					while (ret.Right.IsOverlay)
					{
						try { ret.Right.SetState(NinjaTrader.NinjaScript.State.Finalized); } catch {}
						ret.Right = RandomIndicator(random);
					}

				// make sure .State >= .Configure
				try
				{
					ret.Left.SetState(NinjaTrader.NinjaScript.State.Configure);
				}
				catch (Exception exp)
				{
					Cbi.Log.Process(typeof(Resource), "CbiUnableToCreateInstance2", new object[] { ret.Left.Name, exp.InnerException != null ? exp.InnerException.ToString() : exp.ToString() }, Cbi.LogLevel.Error, Cbi.LogCategories.Default);
					ret.Left.SetState(NinjaTrader.NinjaScript.State.Finalized);
					return null;
				}

				try
				{
					ret.Right.SetState(NinjaTrader.NinjaScript.State.Configure);
				}
				catch (Exception exp)
				{
					Cbi.Log.Process(typeof(Resource), "CbiUnableToCreateInstance2", new object[] { ret.Right.Name, exp.InnerException != null ? exp.InnerException.ToString() : exp.ToString() }, Cbi.LogLevel.Error, Cbi.LogCategories.Default);
					ret.Right.SetState(NinjaTrader.NinjaScript.State.Finalized);
					return null;
				}

				return ret;
			}
		}

		internal ChartPattern RandomCandleStickPattern(Random random)
		{
			return StrategyGenerator.SelectedCandleStickPattern[random.Next(StrategyGenerator.SelectedCandleStickPattern.Length - 1)];
		}

		internal IndicatorBase RandomIndicator(Random random)
		{
			IndicatorBase	ret;
			Type			type = null;
			while (true)
			{
				try
				{
					type	= StrategyGenerator.SelectedIndicatorTypes[random.Next(StrategyGenerator.SelectedIndicatorTypes.Length - 1)];
					ret		= (IndicatorBase) type.Assembly.CreateInstance(type.FullName);
					ret.SetState(State.Configure);

					if (!ret.VerifyVendorLicense()			// make sure to not run into vendor license violations
						|| ret.BarsPeriods.Length > 1		// multi-series are not supported (yet)
						|| ret.Values.Length == 0)			// needed to have at least one plot
					{
						try { ret.SetState(State.Finalized); } catch {}
						continue;
					}

					ret.SelectedValueSeries = random.Next(ret.Values.Length);

					return ret;
				}
				catch (Exception exp)
				{
					Cbi.Log.Process(typeof(Resource), "CbiUnableToCreateInstance2", new object[] { type.FullName, exp.InnerException != null ? exp.InnerException.ToString() : exp.ToString() }, Cbi.LogLevel.Error, Cbi.LogCategories.Default);
				}
			}
		}

		public int SessionMinutesForLongEntries
		{ get; set; }

		public int SessionMinutesForLongExits
		{ get; set; }

		public int SessionMinutesForShortEntries
		{ get; set; }

		public int SessionMinutesForShortExits
		{ get; set; }

		public int SessionMinutesOffsetForLongEntries
		{ get; set; }

		public int SessionMinutesOffsetForLongExits
		{ get; set; }

		public int SessionMinutesOffsetForShortEntries
		{ get; set; }

		public int SessionMinutesOffsetForShortExits
		{ get; set; }

		public double StopLossPercent
		{ get; set; }

		/// <summary>
		/// Create a hard coded version of the strategy.
		/// </summary>
		/// <param name="templateStrategy">Optional template strategy</param>
		/// <returns>The strategy code</returns>
		public override string ToString(StrategyBase templateStrategy = null)
		{
			StringBuilder s = new StringBuilder();

			if (EnterLongCondition	!= null)			EnterLongCondition.PrintAddChartIndicator(this, s, 4);
			if (EnterShortCondition	!= null)			EnterShortCondition.PrintAddChartIndicator(this, s, 4);
			if (ExitLongCondition	!= null)			ExitLongCondition.PrintAddChartIndicator(this, s, 4);
			if (ExitShortCondition	!= null)			ExitShortCondition.PrintAddChartIndicator(this, s, 4);
			string addChartIndicator = s.ToString();
			
			s = new StringBuilder();
			s.Append("//" + Environment.NewLine);
			s.Append("// Copyright (C) 2022, NinjaTrader LLC <www.ninjatrader.com>." + Environment.NewLine);
			s.Append("// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release." + Environment.NewLine);
			s.Append("//" + Environment.NewLine);
			s.Append("#region Using declarations" + Environment.NewLine);
			s.Append("using System;" + Environment.NewLine);
			s.Append("using System.Collections.Generic;" + Environment.NewLine);
			s.Append("using System.ComponentModel;" + Environment.NewLine);
			s.Append("using System.ComponentModel.DataAnnotations;" + Environment.NewLine);
			s.Append("using System.Linq;" + Environment.NewLine);
			s.Append("using System.Text;" + Environment.NewLine);
			s.Append("using System.Threading.Tasks;" + Environment.NewLine);
			s.Append("using System.Windows;" + Environment.NewLine);
			s.Append("using System.Windows.Input;" + Environment.NewLine);
			s.Append("using System.Windows.Media;" + Environment.NewLine);
			s.Append("using System.Xml.Serialization;" + Environment.NewLine);
			s.Append("using NinjaTrader.Cbi;" + Environment.NewLine);
			s.Append("using NinjaTrader.Gui;" + Environment.NewLine);
			s.Append("using NinjaTrader.Gui.Chart;" + Environment.NewLine);
			s.Append("using NinjaTrader.Gui.SuperDom;" + Environment.NewLine);
			s.Append("using NinjaTrader.Data;" + Environment.NewLine);
			s.Append("using NinjaTrader.NinjaScript;" + Environment.NewLine);
			s.Append("using NinjaTrader.Core.FloatingPoint;" + Environment.NewLine);
			s.Append("using NinjaTrader.NinjaScript.Indicators;" + Environment.NewLine);
			s.Append("using NinjaTrader.NinjaScript.DrawingTools;" + Environment.NewLine);
			s.Append("#endregion" + Environment.NewLine + Environment.NewLine);

			s.Append("// This namespace holds strategies in this folder and is required. Do not change it." + Environment.NewLine);
			s.Append("namespace NinjaTrader.NinjaScript.Strategies" + Environment.NewLine);
			s.Append("{" + Environment.NewLine);
			s.Indent(1);		s.Append("public class " + (templateStrategy is IGeneratedStrategy || templateStrategy != null ? templateStrategy.Name : "GeneratedStrategy") + " : " + (templateStrategy is IGeneratedStrategy ? templateStrategy.Name : "Strategy") + Environment.NewLine);
			s.Indent(1);		s.Append("{" + Environment.NewLine);
			if (HasCandleStickPatternExpression)
				{ s.Indent(2);	s.Append("private Indicators.CandleStickPatternLogic candleStickPatternLogic;" + Environment.NewLine); }
			if (SessionMinutesOffsetForLongEntries >= 0)
				{ s.Indent(2);	s.Append("private DateTime                           endTimeForLongEntries;" + Environment.NewLine); }
			if (SessionMinutesOffsetForLongExits >= 0)
				{ s.Indent(2);	s.Append("private DateTime                           endTimeForLongExits;" + Environment.NewLine); }
			if (SessionMinutesOffsetForShortEntries >= 0)
				{ s.Indent(2);	s.Append("private DateTime                           endTimeForShortEntries;" + Environment.NewLine); }
			if (SessionMinutesOffsetForShortExits >= 0)
				{ s.Indent(2);	s.Append("private DateTime                           endTimeForShortExits;" + Environment.NewLine); }
			if (SessionMinutesOffsetForLongEntries >= 0 || SessionMinutesOffsetForShortEntries >= 0 || SessionMinutesOffsetForLongExits >= 0 || SessionMinutesOffsetForShortExits >= 0)
				{ s.Indent(2);	s.Append("private Data.SessionIterator               sessionIterator;" + Environment.NewLine); }
			if (SessionMinutesOffsetForLongEntries >= 0)
				{ s.Indent(2);	s.Append("private DateTime                           startTimeForLongEntries;" + Environment.NewLine); }
			if (SessionMinutesOffsetForLongExits >= 0)
				{ s.Indent(2);	s.Append("private DateTime                           startTimeForLongExits;" + Environment.NewLine); }
			if (SessionMinutesOffsetForShortEntries >= 0)
				{ s.Indent(2);	s.Append("private DateTime                           startTimeForShortEntries;" + Environment.NewLine); }
			if (SessionMinutesOffsetForShortExits >= 0)
				{ s.Indent(2);	s.Append("private DateTime                           startTimeForShortExits;" + Environment.NewLine); }
			s.Indent(2);		s.Append(Environment.NewLine);

			s.Indent(2);		s.Append("protected override void OnStateChange()" + Environment.NewLine);
			s.Indent(2);		s.Append("{" + Environment.NewLine);
			s.Indent(3);		s.Append("base.OnStateChange();" + Environment.NewLine + Environment.NewLine);
			s.Indent(3);		s.Append("if (State == State.SetDefaults)" + Environment.NewLine);
			s.Indent(3);		s.Append("{" + Environment.NewLine);
			s.Indent(4);		s.Append("IncludeTradeHistoryInBacktest             = false;" + Environment.NewLine);
			if (ExitOnSessionClose.HasValue)
				{ s.Indent(4);	s.Append("IsExitOnSessionCloseStrategy              = " + ExitOnSessionClose.Value.ToString().ToLower() + ";" + Environment.NewLine); }
			s.Indent(4);		s.Append("IsInstantiatedOnEachOptimizationIteration = true;" + Environment.NewLine);
			s.Indent(4);		s.Append("MaximumBarsLookBack                       = MaximumBarsLookBack.TwoHundredFiftySix;" + Environment.NewLine);
			if (templateStrategy != null)
				{ s.Indent(4);	s.Append("Name                                      = \"" + templateStrategy.Name + "\";" + Environment.NewLine); }
			s.Indent(4);		s.Append("SupportsOptimizationGraph                 = false;" + Environment.NewLine);
			s.Indent(3);		s.Append("}" + Environment.NewLine);
			s.Indent(3);		s.Append("else if (State == State.Configure)" + Environment.NewLine);
			s.Indent(3);		s.Append("{" + Environment.NewLine);
			if (HasCandleStickPatternExpression)
				{ s.Indent(4);	s.Append("candleStickPatternLogic = new CandleStickPatternLogic(this, " + TrendStrength + ");" + Environment.NewLine); }
			if (!double.IsNaN(ParabolicStopPercent))
				{ s.Indent(4);	s.Append("SetParabolicStop(CalculationMode.Percent, " + ParabolicStopPercent.ToString(CultureInfo.InvariantCulture) + ");" + Environment.NewLine); }
			if (!double.IsNaN(ProfitTargetPercent))
				{ s.Indent(4);	s.Append("SetProfitTarget(CalculationMode.Percent, " + ProfitTargetPercent.ToString(CultureInfo.InvariantCulture) + ");" + Environment.NewLine); }
			if (!double.IsNaN(StopLossPercent))
				{ s.Indent(4);	s.Append("SetStopLoss(CalculationMode.Percent, " + StopLossPercent.ToString(CultureInfo.InvariantCulture) + ");" + Environment.NewLine); }
			if (!double.IsNaN(TrailStopPercent))
				{ s.Indent(4);	s.Append("SetTrailStop(CalculationMode.Percent, " + TrailStopPercent.ToString(CultureInfo.InvariantCulture) + ");" + Environment.NewLine); }
			s.Indent(3);		s.Append("}" + Environment.NewLine);

			if (!string.IsNullOrEmpty(addChartIndicator))
			{
				s.Indent(3);		s.Append("else if (State == State.DataLoaded)" + Environment.NewLine);
				s.Indent(3);		s.Append("{" + Environment.NewLine);
				s.Append(addChartIndicator);
				s.Indent(3);		s.Append("}" + Environment.NewLine);
			}
			s.Indent(2);		s.Append("}" + Environment.NewLine + Environment.NewLine);

			s.Indent(2);		s.Append("protected override void OnBarUpdate()" + Environment.NewLine);
			s.Indent(2);		s.Append("{" + Environment.NewLine);
			s.Indent(3);		s.Append("base.OnBarUpdate();" + Environment.NewLine + Environment.NewLine);

			s.Indent(3);		s.Append("if (CurrentBars[0] < BarsRequiredToTrade)" + Environment.NewLine);
			s.Indent(4);		s.Append("return;" + Environment.NewLine + Environment.NewLine);

			if (SessionMinutesOffsetForLongEntries >= 0 || SessionMinutesOffsetForShortEntries >= 0 || SessionMinutesOffsetForLongExits >= 0 || SessionMinutesOffsetForShortExits >= 0)
			{
				s.Indent(3); 	s.Append("if (sessionIterator == null || BarsArray[0].IsFirstBarOfSession)" + Environment.NewLine);
				s.Indent(3); 	s.Append("{" + Environment.NewLine);
				s.Indent(4); 	s.Append("if (sessionIterator == null)" + Environment.NewLine);
				s.Indent(4); 	s.Append("{" + Environment.NewLine);
				s.Indent(5);	s.Append("sessionIterator = new Data.SessionIterator(BarsArray[0]);" + Environment.NewLine);
				s.Indent(5);	s.Append("sessionIterator.GetNextSession(Times[0][0], true);" + Environment.NewLine);
				s.Indent(4); 	s.Append("}" + Environment.NewLine);
				s.Indent(4); 	s.Append("else if (BarsArray[0].IsFirstBarOfSession)" + Environment.NewLine);
				s.Indent(5);	s.Append("sessionIterator.GetNextSession(Times[0][0], true);" + Environment.NewLine + Environment.NewLine);

				if (SessionMinutesOffsetForLongEntries >= 0)
				{
					s.Indent(4);s.Append("startTimeForLongEntries   = sessionIterator.ActualSessionBegin.AddMinutes(" + SessionMinutesOffsetForLongEntries + ");" + Environment.NewLine);
					s.Indent(4);s.Append("endTimeForLongEntries     = startTimeForLongEntries.AddMinutes(" + SessionMinutesForLongEntries + ");" + Environment.NewLine);
				}

				if (SessionMinutesOffsetForShortEntries >= 0)
				{
					s.Indent(4);s.Append("startTimeForShortEntries  = sessionIterator.ActualSessionBegin.AddMinutes(" + SessionMinutesOffsetForShortEntries + ");" + Environment.NewLine);
					s.Indent(4);s.Append("endTimeForShortEntries    = startTimeForShortEntries.AddMinutes(" + SessionMinutesForShortEntries + ");" + Environment.NewLine);
				}

				if (SessionMinutesOffsetForLongExits >= 0)
				{
					s.Indent(4);s.Append("startTimeForLongExits     = sessionIterator.ActualSessionEnd.AddMinutes(-" + (SessionMinutesOffsetForLongExits + SessionMinutesForLongExits) + ");" + Environment.NewLine);
					s.Indent(4);s.Append("endTimeForLongExits       = startTimeForLongExits.AddMinutes(" + SessionMinutesForLongExits + ");" + Environment.NewLine);
				}

				if (SessionMinutesOffsetForShortExits >= 0)
				{
					s.Indent(4);s.Append("startTimeForShortExits    = sessionIterator.ActualSessionEnd.AddMinutes(-" + (SessionMinutesOffsetForShortExits + SessionMinutesForShortExits) + ");" + Environment.NewLine);
					s.Indent(4);s.Append("endTimeForShortExits      = startTimeForShortExits.AddMinutes(" + SessionMinutesForShortExits + ");" + Environment.NewLine);
				}

				s.Indent(3); 	s.Append("}" + Environment.NewLine + Environment.NewLine);
			}

			bool additionalNewLine = false;
			if (EnterLongCondition != null || SessionMinutesOffsetForLongEntries >= 0)
			{
				s.Indent(3);
				s.Append("if (");
				if (EnterLongCondition != null)
					EnterLongCondition.Print(s, 3 + 1);
				if (SessionMinutesOffsetForLongEntries >= 0)
				{
					if (EnterLongCondition != null)
					{
						s.Append(Environment.NewLine);
						s.Indent(4);
						s.Append("&& ");
					}
					s.Append("startTimeForLongEntries < Times[0][0] && Times[0][0] <= endTimeForLongEntries");
				}
				if (EnterOnDayOfWeek != null)
				{
					bool isFirst = true;
					for (int i = 0; i < EnterOnDayOfWeek.Length; i++)
					{
						if (EnterOnDayOfWeek[i] == true)
						{
							if (isFirst)
							{
								s.Append(Environment.NewLine);
								s.Indent(4);
								s.Append("&& (");

								isFirst = false;
							}
							else
								s.Append(" || ");
							s.Append("Times[0][0].DayOfWeek == DayOfWeek." + Enum.GetValues(typeof(DayOfWeek)).GetValue(i));
						}
					}
					if (!isFirst)
						s.Append(")");
				}
				s.Append(")" + Environment.NewLine);
				s.Indent(4);
				s.Append((templateStrategy is IGeneratedStrategy ? "OnEnterLong();" : "EnterLong();") + Environment.NewLine);

				additionalNewLine = true;
			}

			if (ExitLongCondition != null || SessionMinutesOffsetForLongExits >= 0)
			{
				if (additionalNewLine)
					s.Append(Environment.NewLine);

				s.Indent(3);
				s.Append("if (");
				if (ExitLongCondition != null)
					ExitLongCondition.Print(s, 3 + 1);
				if (SessionMinutesOffsetForLongExits >= 0)
				{
					if (ExitLongCondition != null)
					{
						s.Append(Environment.NewLine);
						s.Indent(4);
						s.Append("&& ");
					}
					s.Append("startTimeForLongExits < Times[0][0] && Times[0][0] <= endTimeForLongExits");
				}
				if (ExitOnDayOfWeek != null)
				{
					bool isFirst = true;
					for (int i = 0; i < ExitOnDayOfWeek.Length; i++)
					{
						if (ExitOnDayOfWeek[i] == true)
						{
							if (isFirst)
							{
								s.Append(Environment.NewLine);
								s.Indent(4);
								s.Append("&& (");

								isFirst = false;
							}
							else
								s.Append(" || ");
							s.Append("Times[0][0].DayOfWeek == DayOfWeek." + Enum.GetValues(typeof(DayOfWeek)).GetValue(i));
						}
					}
					if (!isFirst)
						s.Append(")");
				}
				s.Append(")" + Environment.NewLine);
				s.Indent(4);
				s.Append((templateStrategy is IGeneratedStrategy ? "OnExitLong();" : "ExitLong();") + Environment.NewLine);

				additionalNewLine = true;
			}

			if (EnterShortCondition != null || SessionMinutesOffsetForShortEntries >= 0)
			{
				if (additionalNewLine)
					s.Append(Environment.NewLine);

				s.Indent(3);
				s.Append("if (");
				if (EnterShortCondition != null)
					EnterShortCondition.Print(s, 3 + 1);
				if (SessionMinutesOffsetForShortEntries >= 0)
				{
					if (EnterShortCondition != null)
					{
						s.Append(Environment.NewLine);
						s.Indent(4);
						s.Append("&& ");
					}
					s.Append("startTimeForShortEntries < Times[0][0] && Times[0][0] <= endTimeForShortEntries");
				}
				if (EnterOnDayOfWeek != null)
				{
					bool isFirst = true;
					for (int i = 0; i < EnterOnDayOfWeek.Length; i++)
					{
						if (EnterOnDayOfWeek[i] == true)
						{
							if (isFirst)
							{
								s.Append(Environment.NewLine);
								s.Indent(4);
								s.Append("&& (");

								isFirst = false;
							}
							else
								s.Append(" || ");
							s.Append("Times[0][0].DayOfWeek == DayOfWeek." + Enum.GetValues(typeof(DayOfWeek)).GetValue(i));
						}
					}
					if (!isFirst)
						s.Append(")");
				}
				s.Append(")" + Environment.NewLine);
				s.Indent(4);
				s.Append((templateStrategy is IGeneratedStrategy ? "OnEnterShort();" : "EnterShort();") + Environment.NewLine);

				additionalNewLine = true;
			}

			if (ExitShortCondition != null || SessionMinutesOffsetForShortExits >= 0)
			{
				if (additionalNewLine)
					s.Append(Environment.NewLine);

				s.Indent(3);
				s.Append("if (");
				if (ExitShortCondition != null)
					ExitShortCondition.Print(s, 3 + 1);
				if (SessionMinutesOffsetForShortExits >= 0)
				{
					if (ExitShortCondition != null)
					{
						s.Append(Environment.NewLine);
						s.Indent(4);
						s.Append("&& ");
					}
					s.Append("startTimeForShortExits < Times[0][0] && Times[0][0] <= endTimeForShortExits");
				}
				if (ExitOnDayOfWeek != null)
				{
					bool isFirst = true;
					for (int i = 0; i < ExitOnDayOfWeek.Length; i++)
					{
						if (ExitOnDayOfWeek[i] == true)
						{
							if (isFirst)
							{
								s.Append(Environment.NewLine);
								s.Indent(4);
								s.Append("&& (");

								isFirst = false;
							}
							else
								s.Append(" || ");
							s.Append("Times[0][0].DayOfWeek == DayOfWeek." + Enum.GetValues(typeof(DayOfWeek)).GetValue(i));
						}
					}
					if (!isFirst)
						s.Append(")");
				}
				s.Append(")" + Environment.NewLine);
				s.Indent(4);
				s.Append((templateStrategy is IGeneratedStrategy ? "OnExitShort();" : "ExitShort();") + Environment.NewLine);

				additionalNewLine = true;
			}

			s.Indent(2);		s.Append("}" + Environment.NewLine);
			s.Indent(1);		s.Append("}" + Environment.NewLine);
			s.Append("}" + Environment.NewLine);

			return s.ToString();
		}

		/// <summary>
		/// Serialize to XML
		/// </summary>
		/// <returns></returns>
		public override XElement ToXml()
		{
			XElement ret = new XElement(GetType().Name);

			// This node is mandatory. Make sure it holds the proper Type.FullName
			ret.Add(new XElement("ClassName", GetType().FullName));

			if (EnterLongCondition	!= null)	ret.Add(new XElement("EnterLongCondition",	EnterLongCondition.ToXml()));
			if (EnterShortCondition	!= null)	ret.Add(new XElement("EnterShortCondition",	EnterShortCondition.ToXml()));
			if (ExitLongCondition	!= null)	ret.Add(new XElement("ExitLongCondition",	ExitLongCondition.ToXml()));
			if (ExitShortCondition	!= null)	ret.Add(new XElement("ExitShortCondition",	ExitShortCondition.ToXml()));

			if (EnterOnDayOfWeek != null)
				ret.Add(new XElement("EnterOnDayOfWeek",				EnterOnDayOfWeek.Select(e => e ? "1" : "0")));

			if (ExitOnDayOfWeek != null)
				ret.Add(new XElement("ExitOnDayOfWeek",					ExitOnDayOfWeek.Select(e => e ? "1" : "0")));

			if (ExitOnSessionClose.HasValue)
				ret.Add(new XElement("ExitOnSessionClose",				ExitOnSessionClose.Value.ToString(CultureInfo.InvariantCulture)));

			ret.Add(new XElement("IsLong",								IsLong.ToString(CultureInfo.InvariantCulture)));
			ret.Add(new XElement("IsShort",								IsShort.ToString(CultureInfo.InvariantCulture)));
			ret.Add(new XElement("ParabolicStopPercent",				ParabolicStopPercent.ToString(CultureInfo.InvariantCulture)));
			ret.Add(new XElement("ProfitTargetPercent",					ProfitTargetPercent.ToString(CultureInfo.InvariantCulture)));
			ret.Add(new XElement("SessionMinutesForLongEntries",		SessionMinutesForLongEntries.ToString(CultureInfo.InvariantCulture)));
			ret.Add(new XElement("SessionMinutesForLongExits",			SessionMinutesForLongExits.ToString(CultureInfo.InvariantCulture)));
			ret.Add(new XElement("SessionMinutesForShortEntries",		SessionMinutesForShortEntries.ToString(CultureInfo.InvariantCulture)));
			ret.Add(new XElement("SessionMinutesForShortExits",			SessionMinutesForShortExits.ToString(CultureInfo.InvariantCulture)));
			ret.Add(new XElement("SessionMinutesOffsetForLongEntries",	SessionMinutesOffsetForLongEntries.ToString(CultureInfo.InvariantCulture)));
			ret.Add(new XElement("SessionMinutesOffsetForLongExits",	SessionMinutesOffsetForLongExits.ToString(CultureInfo.InvariantCulture)));
			ret.Add(new XElement("SessionMinutesOffsetForShortEntries",	SessionMinutesOffsetForShortEntries.ToString(CultureInfo.InvariantCulture)));
			ret.Add(new XElement("SessionMinutesOffsetForShortExits",	SessionMinutesOffsetForShortExits.ToString(CultureInfo.InvariantCulture)));
			ret.Add(new XElement("StopLossPercent",						StopLossPercent.ToString(CultureInfo.InvariantCulture)));
			ret.Add(new XElement("TrailStopPercent",					TrailStopPercent.ToString(CultureInfo.InvariantCulture)));
			ret.Add(new XElement("TrendStrength",						TrendStrength.ToString(CultureInfo.InvariantCulture)));

			return ret;
		}

		public double TrailStopPercent
		{ get; set; }

		public int TrendStrength
		{ get; set; }
		
		/// <summary>
		/// Constructor with no parameters is mandatory for any subclass of .GeneratedStrategyLogicBase
		/// </summary>
		public GeneratedStrategyLogic()
		{
			ChartIndicators						= new List<string>();
			EnterOnDayOfWeek					= null;
			ExitOnDayOfWeek						= null;
			ExitOnSessionClose					= new bool?();
			Id									= Interlocked.Increment(ref lastId);
			ParabolicStopPercent				= double.NaN;
			PriorPerformance					= double.MinValue;
			ProfitTargetPercent					= double.NaN;
			SessionMinutesForLongEntries		= -1;
			SessionMinutesForLongExits			= -1;
			SessionMinutesForShortEntries		= -1;
			SessionMinutesForShortExits			= -1;
			SessionMinutesOffsetForLongEntries	= -1;
			SessionMinutesOffsetForLongExits	= -1;
			SessionMinutesOffsetForShortEntries	= -1;
			SessionMinutesOffsetForShortExits	= -1;
			StopLossPercent						= double.NaN;
			TrailStopPercent					= double.NaN;
			TrendStrength						= 4;
		}

		/// <summary>
		/// The expression tree has some properties which could be mutated linearly like 'oldValue = 10, newValue = 10 +- 1'
		/// In case we had a 'linear' mutation which yielded better results than the prior generation, then we wanted to try 'more of the same'.
		/// This implies that prior random triggers (see .r0/1/2/3...) needed to be re-applied and not be calculated again.
		/// </summary>
		public bool TryLinearMutation
		{ get; set; }

		public Optimizers.StrategyGenerator StrategyGenerator
		{ get; set; }
	}
}

