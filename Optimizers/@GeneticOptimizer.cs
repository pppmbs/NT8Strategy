// 
// Copyright (C) 2020, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
using System;
using System.ComponentModel.DataAnnotations;
using System.Text;
using NinjaTrader.Core.FloatingPoint;

#endregion

namespace NinjaTrader.NinjaScript.Optimizers
{
	public class GeneticOptimizer : Optimizer
	{
		private		double			averagePerformance;
		private		Individual[]	bestResultsGeneration;
		private		int				crossoverIndividuals;
		private		double			crossoverRate;
		private		Individual[]	currentGeneration;
		private		double			maxGenPerformance;
		private		double			minGenPerformance;
		private		double			mutationRate;
		private		double			mutationStrength;
		private		Random			random; 
		private		int				resetIndividuals;
		private		double			resetSize;
		private		int				stabilityIndividuals;
		private		double			stabilityScore;
		private		double			stabilitySize;
		private		double			totalPerformance;
		private		int				trueBestResults;
		private		int				trueGenerations;
		private		int				trueGenerationSize;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				ConvergenceThreshold		= 20;
				CrossoverRatePecent			= 80;
				Generations					= 5;
				GenerationSize				= 25;
				MinimumPerformance			= 0.0;
				Name						= Custom.Resource.NinjaScriptOptimizerGenetic;
				MutationRatePercent			= 2;
				MutationStrengthPercent		= 25;
				ResetSizePercent			= 3;
				StabilitySizePercent		= 4;

				random						= new Random(Core.Globals.Now.Millisecond);
				stabilityScore				= 0.0;
			}
			else if (State == State.Configure)
			{
				int wantedIterations	= Generations * GenerationSize;
				long possibleIterations	= GetParametersCombinationsCount(Strategies[0]);
				NumberOfIterations		= Math.Min(wantedIterations, possibleIterations);
				trueBestResults			= (int)Math.Min(KeepBestResults, NumberOfIterations);

				if (possibleIterations < wantedIterations)
				{
					trueGenerationSize		= (int)Math.Min(GenerationSize, possibleIterations);
					int gen					= 0;
					int runningIndividuals	= 0;
					while (gen < Generations && runningIndividuals < possibleIterations)
					{
						gen++;
						runningIndividuals += trueGenerationSize;
					}
					trueGenerations = gen;
				}
				else
				{
					trueGenerationSize	= GenerationSize;
					trueGenerations		= Generations;
				}
				crossoverIndividuals		= Math.Min((int)(trueGenerationSize * crossoverRate), trueGenerationSize);
				stabilityIndividuals		= Math.Max(1, (int)(trueBestResults * stabilitySize));
				resetIndividuals			= Math.Max(1, (int)(trueBestResults * resetSize));
			}
		}

		protected override void OnOptimize()
		{
			if (Strategies[0].OptimizationParameters.Count == 0)
				return;

			bestResultsGeneration	= CreateHolder(trueBestResults);

			Generation[] generations = new Generation[trueGenerations];
			for (int j = 0; j < trueGenerations; j++)
				generations[j] = new Generation(j);

			double	previousPerformance	= 0.0;
			double	previousStability	= 0.0;
			bool	stabilityReached	= false;

			for (int i = 0; i < trueGenerations; i++)
			{
				currentGeneration = CreateHolder(trueGenerationSize);
				Reset();
				int idx;

				if (i == 0)
					idx = CreateRandomIndividuals(0, trueGenerationSize);
				else
				{
					if ((MinimumPerformance.ApproxCompare(0) == 0 || maxGenPerformance <= MinimumPerformance) && averagePerformance.ApproxCompare(0) != 0)
					{
						if (stabilityReached)
						{
							generations[i - 1]	.IsStable	= true;
							generations[i]		.IsReset	= true;

							idx = AddSurvivors(resetIndividuals);

							if (idx < trueGenerationSize - 1)
								idx = CreateRandomIndividuals(idx, trueGenerationSize);

							previousStability = 0.0;
						}
						else
						{
							idx = CreateCrossoverIndividuals(0, crossoverIndividuals);
							idx = AddSurvivorsCheckDuplicate(idx, Math.Min(trueGenerationSize - 1, idx + trueBestResults));
							idx = CreateRandomIndividuals(idx, trueGenerationSize);
						}
					}
					else if (generations[i - 1].AveragePerformance.ApproxCompare(0) == 0)
					{
						idx = CreateRandomIndividuals(0, trueGenerationSize);
					}
					else
						break;
				}

				generations[i].AnalyzeInput(currentGeneration);
				Cbi.SystemPerformance[] individualPerformances = OptimizeIndividuals(currentGeneration, idx - 1);
				CreateGeneration(individualPerformances);

				if (RankPopulation().ApproxCompare(1) == 0 && averagePerformance.ApproxCompare(0) != 0)
					break;

				stabilityReached = (previousStability.ApproxCompare(stabilityScore) == 0);

				generations[i].AveragePerformance	= averagePerformance;
				generations[i].MaxPerformance		= maxGenPerformance;
				generations[i].MinPerformance		= minGenPerformance;
				generations[i].StabilityScore		= stabilityScore;
				generations[i].TotalPerformance		= totalPerformance;

				if (previousPerformance.ApproxCompare(0) != 0)
					generations[i].PercentImprovement = (generations[i].TotalPerformance - previousPerformance) / previousPerformance;

				previousStability	= stabilityScore;
				previousPerformance	= totalPerformance;
			}
		}

		private int AddSurvivors(int addCount)
		{
			int endidx	= Math.Min(addCount, trueGenerationSize);
			for (int idx = 0; idx < endidx; idx++)
				if (bestResultsGeneration[idx].Type != Individual.IndividualType.Unknown)
					CopyIndividual(bestResultsGeneration[idx], currentGeneration[idx]);
			return endidx;
		}

		private int AddSurvivorsCheckDuplicate(int nextGenIdx, int addCount)
		{
			int endidx		= Math.Min(nextGenIdx + addCount, trueGenerationSize);
			int curgenidx	= 0;

			while (nextGenIdx < endidx && curgenidx < trueBestResults)
			{
				if (bestResultsGeneration[curgenidx].Type != Individual.IndividualType.Unknown && !ContainsDuplicate(currentGeneration, bestResultsGeneration[curgenidx], nextGenIdx))
				{
					CopyIndividual(bestResultsGeneration[curgenidx], currentGeneration[nextGenIdx]);
					nextGenIdx++;
				}
				curgenidx++;
			}

			return nextGenIdx;
		}

		private static void CopyIndividual(Individual from, Individual to)
		{
			to.Type				= from.Type;
			to.PerformanceValue	= from.PerformanceValue;
			to.Weight			= from.Weight;

			for (int i = 0; i < to.Parameters.GetUpperBound(0) + 1; i++)
				from.Parameters[i].CopyTo(to.Parameters[i]);
		}

		private int CreateCrossoverIndividuals(int nextGenIdx, int addCount)
		{
			int convergenceCounter	= 0;
			int endidx				= Math.Min(nextGenIdx + addCount, trueGenerationSize);
			int lastFilled			= nextGenIdx - 1;

			while (convergenceCounter < ConvergenceThreshold && nextGenIdx < endidx)
			{
				int			parent1			= RouletteSelection(bestResultsGeneration);
				int			parent2			= RouletteSelection(bestResultsGeneration, parent1);
				int			forwardGenIdx	= Math.Min(nextGenIdx + 1, currentGeneration.Length - 1);
				Individual	child1			= new Individual(Strategies[0].OptimizationParameters.Count);
				Individual	child2			= new Individual(Strategies[0].OptimizationParameters.Count);

				Crossover(bestResultsGeneration[parent1], bestResultsGeneration[parent2], child1, child2);

				child1.Type	= Individual.IndividualType.Child;
				child2.Type	= Individual.IndividualType.Child;

				MutateIndividual(child1);
				MutateIndividual(child2);

				if (!ContainsDuplicate(bestResultsGeneration, child1, trueBestResults) && !ContainsDuplicate(currentGeneration, child1, currentGeneration.Length))
				{
					currentGeneration[nextGenIdx] = child1;
					if (!ContainsDuplicate(bestResultsGeneration, child2, nextGenIdx) && !ContainsDuplicate(currentGeneration, child2, currentGeneration.Length))
					{
						currentGeneration[forwardGenIdx] = child2;
						nextGenIdx += 2;
						lastFilled = forwardGenIdx;
					}
					else
					{
						lastFilled = nextGenIdx;
						nextGenIdx += 1;
					}
				}
				else if (!ContainsDuplicate(bestResultsGeneration, child2, trueBestResults) && !ContainsDuplicate(currentGeneration, child2, currentGeneration.Length))
				{
					currentGeneration[nextGenIdx] = child2;
					lastFilled = nextGenIdx;
					nextGenIdx += 1;
				}
				else
					convergenceCounter++;
			}

			return lastFilled + 1;
		}

		private void CreateGeneration(Cbi.SystemPerformance[] results)
		{
			stabilityScore		= 0.0;
			totalPerformance	= 0.0;
			averagePerformance	= 0.0;

			for (int i = 0; i < results.Length; i++)
			{
				// This can happen due to two things: strategy is aborted, and when
				// keep best# results is greater than the # of iterations ran, eg
				// SampleMaCrossover with Fast 10;15;1 (6 iterations) and keep# results 10
				if (results[i].ParameterValues == null)
					continue; 

				for (int j = 0; j < Strategies[0].OptimizationParameters.Count; j++)
				{
					Parameter parameter = Strategies[0].OptimizationParameters[j];
					if (parameter.ParameterType == typeof(int))
						parameter.Value = (int)results[i].ParameterValues[j];
					else if (parameter.ParameterType == typeof(double))
						parameter.Value = (double)results[i].ParameterValues[j];
					else if (parameter.ParameterType == typeof(bool))
						parameter.Value = (bool)results[i].ParameterValues[j];
					else if (parameter.ParameterType.IsEnum)
						parameter.Value = results[i].ParameterValues[j];

					parameter.CopyTo(bestResultsGeneration[i].Parameters[j]);
				}

				bestResultsGeneration[i].PerformanceValue = results[i].PerformanceValue;

				if (maxGenPerformance < results[i].PerformanceValue)
					maxGenPerformance = results[i].PerformanceValue;
				if (minGenPerformance.ApproxCompare(0) == 0 || minGenPerformance > results[i].PerformanceValue)
					minGenPerformance = results[i].PerformanceValue;
				if (i < stabilityIndividuals)
					stabilityScore += results[i].PerformanceValue;

				totalPerformance += results[i].PerformanceValue;
				bestResultsGeneration[i].Type = Individual.IndividualType.Parent;
			}

			averagePerformance = totalPerformance / trueBestResults;
		}

		private Individual[] CreateHolder(int size)
		{
			Individual[] individuals = new Individual[size];

			for (int i = 0; i < size; i++)
				individuals[i] = new Individual(Strategies[0].OptimizationParameters.Count);

			return individuals;
		}

		private int CreateRandomIndividuals(int nextGenIdx, int addCount)
		{
			int convergenceCounter	= 0;
			int endidx				= Math.Min(nextGenIdx + addCount, trueGenerationSize);

			while (convergenceCounter < ConvergenceThreshold && nextGenIdx < endidx)
			{
				Individual tmp = new Individual(Strategies[0].OptimizationParameters.Count);
				for (int j = 0; j < Strategies[0].OptimizationParameters.Count; j++)
				{
					Parameter parameter = Strategies[0].OptimizationParameters[j];

					if (parameter.ParameterType == typeof (int))
					{
						int increment		= Math.Max(1, (int)parameter.Increment);
						int maxSteps		= ((int)parameter.Max - (int)parameter.Min) / increment + 1;
						int randomValue		= random.Next(0, maxSteps);
						parameter.Value		= (int)parameter.Min + randomValue * increment;
					}
					else if (parameter.ParameterType == typeof(double))
					{
						double min			= (double)parameter.Min;
						double max			= (double)parameter.Max;
						double increment	= Math.Max(0.00000001, parameter.Increment);
						int maxSteps		= (int)((max - min) / increment);
						int randomValue		= random.Next(0, maxSteps + 1);
						parameter.Value		= (double)parameter.Min + randomValue * increment;
					}
					else if (parameter.ParameterType == typeof(bool))
					{
						if ((bool)parameter.Min != (bool)parameter.Max)
							parameter.Value = (random.Next(0, 2) == 1);
						else
							parameter.Value = parameter.Min;
					}
					else if (parameter.ParameterType.IsEnum)
						parameter.Value = parameter.EnumValues[random.Next(0, parameter.EnumValues.Length)];

					parameter.CopyTo(tmp.Parameters[j]);
				}

				if (!ContainsDuplicate(currentGeneration, tmp, nextGenIdx + 1))
				{
					tmp.Type = Individual.IndividualType.Random;
					currentGeneration[nextGenIdx] = tmp;
					nextGenIdx++;
				}
				else
					convergenceCounter++;
			}

			return nextGenIdx;
		}

		private void Crossover(Individual parent1, Individual parent2, Individual child1, Individual child2)
		{
			int position = random.Next(Strategies[0].OptimizationParameters.Count);

			for (int i = 0; i < Strategies[0].OptimizationParameters.Count; i++)
			{
				if (i < position)
				{
					parent1.Parameters[i].CopyTo(child1.Parameters[i]);
					parent2.Parameters[i].CopyTo(child2.Parameters[i]);
				}
				else
				{
					parent2.Parameters[i].CopyTo(child1.Parameters[i]);
					parent1.Parameters[i].CopyTo(child2.Parameters[i]);
				}
			}
		}

		private static bool ContainsDuplicate(Individual[] individuals, Individual individual, int idx)
		{
			bool found = false;
			for (int i = Math.Min(idx, individuals.Length) - 1; i >= 0; i--)
			{
				if (individuals[i].IndividualName != individual.IndividualName)
					continue;
				found = true;
				break;
			}

			return found;
		}

		public void MutateIndividual(Individual individual)
		{
			if (!(mutationRate >= random.NextDouble()))
				return;

			for (int j = 0; j < Strategies[0].OptimizationParameters.Count; j++)
			{
				Parameter parameter = individual.Parameters[j];

				if (parameter.ParameterType == typeof(int))
					parameter.Value = Math.Min((int)parameter.Max, Math.Max((int)parameter.Min, (int)parameter.Value + (int)parameter.Increment * (random.Next(0, 3) - 1)));
				else if (parameter.ParameterType == typeof(double))
				{
					double minValue = Math.Max((double)parameter.Min, (double)parameter.Value * (1.0 - mutationStrength));
					double maxValue = Math.Min((double)parameter.Max, (double)parameter.Value * (1.0 + mutationStrength));
					parameter.Value = Math.Min(maxValue, Math.Max(minValue, random.NextDouble() * (maxValue - minValue) + minValue));
				}
				else if (parameter.ParameterType == typeof(bool))
				{
					if ((bool)parameter.Min != (bool)parameter.Max)
						parameter.Value = (random.Next(0, 2) == 1);
					else
						parameter.Value = parameter.Min;
				}
				else if (parameter.ParameterType.IsEnum)
					parameter.Value = parameter.EnumValues[random.Next(0, parameter.EnumValues.Length)];

				parameter.CopyTo(individual.Parameters[j]);
			}
			individual.Type = Individual.IndividualType.Mutant;
		}

		private Cbi.SystemPerformance[] OptimizeIndividuals(Individual[] nextGen, int idx)
		{
			for (int i = 0; i <= idx; i++)
			{
				for (int j = 0; j < Strategies[0].OptimizationParameters.Count; j++)
					 Strategies[0].OptimizationParameters[j].Value	= nextGen[i].Parameters[j].Value;
				RunIteration();
			}

			WaitForIterationsCompleted();
			return Results;
		}

		private double RankPopulation()
		{
			double minPerformance	= bestResultsGeneration[trueBestResults - 1].PerformanceValue;
			double maxPerformance	= bestResultsGeneration[0].PerformanceValue;
			double totalWeight		= 0;
			double denom			= maxPerformance.ApproxCompare(minPerformance) == 0 ? 1 : maxPerformance - minPerformance;

			for (int i = 0; i < trueBestResults; i++)
			{
				bestResultsGeneration[i].Weight = (bestResultsGeneration[i].PerformanceValue - minPerformance) / denom;
				totalWeight += bestResultsGeneration[i].Weight;
			}

			if (totalWeight.ApproxCompare(0) == 0)
				totalWeight = 1;
			bestResultsGeneration[trueBestResults - 1].Weight = 0;
			for (int i = trueBestResults - 2; i >= 0; i--)
				bestResultsGeneration[i].Weight = bestResultsGeneration[i + 1].Weight + bestResultsGeneration[i].Weight / totalWeight;

			return totalWeight;
		}

		private int RouletteSelection(Individual[] individuals, int self)
		{
			int idx;

			do
				idx = RouletteSelection(individuals);
			while (idx == self);

			return idx;
		}

		private int RouletteSelection(Individual[] individuals)
		{
			double	randomWeight	= random.NextDouble();
			int		idx				= -1;
			int		first			= 0;
			int		last			= 0;
			for (int i = 0; i < individuals.Length; i++)
			{
				if (individuals[i].Type == Individual.IndividualType.Unknown)
					break;
				last = i;
			}
			int		mid				= (last - first) / 2;

			while (idx == -1 && first <= last)
			{
				if (randomWeight < individuals[mid].Weight)
					first = mid;
				else
					last = mid;
				mid = (first + last) / 2;
				if (last - first == 1)
					idx = last;
			}

			return idx;
		}

		public class Generation
		{
			public double	AveragePerformance		{ get; set; }
			public int		ChildrenCount			{ get; set; }
			public int		GenerationNumber		{ get; set; }
			public bool		IsStable				{ get; set; }
			public bool		IsReset					{ get; set; }
			public double	MaxPerformance			{ get; set; }
			public double	MinPerformance			{ get; set; }
			public double	MutantCount				{ get; set; }
			public double	PercentImprovement		{ get; set; }
			public int		ParentCount				{ get; set; }
			public int		PopulationCount			{ get; set; }
			public int		RandomCount				{ get; set; }
			public double	StabilityScore			{ get; set; }
			public double	TotalPerformance		{ get; set; }

			public void AnalyzeInput(Individual[] individuals)
			{
				for (int i = 0; i < individuals.GetUpperBound(0) + 1; i++)
				{
					switch (individuals[i].Type)
					{
						case Individual.IndividualType.Unknown:
							continue;
						case Individual.IndividualType.Child:
							ChildrenCount++;
							break;
						case Individual.IndividualType.Mutant:
							ChildrenCount++;
							MutantCount++;
							break;
						case Individual.IndividualType.Parent:
							ParentCount++;
							break;
						case Individual.IndividualType.Random:
							RandomCount++;
							break;
					}
					PopulationCount++;
				}
			}

			public Generation(int generationNum)
			{
				GenerationNumber = generationNum;
			}

			public override string ToString()
			{
				return string.Format("Generation# = {0} Average performance = {1}, %Improvement = {2}, Stability={3}, Perf={4}",
					GenerationNumber, AveragePerformance, PercentImprovement, StabilityScore, TotalPerformance);
			}
		}

		public class Individual : ICloneable
		{
			public IndividualType	Type				{ get; set; }
			public Parameter[]		Parameters			{ get; set; }
			public double			PerformanceValue	{ get; set; }
			public double			Weight				{ get; set; }

			public enum IndividualType
			{
				Child,
				Mutant,
				Parent,
				Random,
				Unknown
			}

			public object Clone()
			{
				return MemberwiseClone();
			}

			public Individual(int parameters)
			{
				Parameters = new Parameter[parameters];

				for (int i = 0; i < parameters; i++)
					Parameters[i] = new Parameter();

				PerformanceValue	= 0.0;
				Weight				= 0.0;
				Type				= IndividualType.Unknown;
			}

			public string IndividualName
			{
				get
				{
					StringBuilder name = new StringBuilder();
					for (int i = 0; i < Parameters.GetUpperBound(0) + 1; i++)
						if (i == 0)
							name.Append(Parameters[i].Value);
						else 
							name.AppendFormat("|{0}", Parameters[i].Value);
					return name.ToString();
				}
			}
		}

		#region UI Properties 
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptGeneticOptimizerConvergenceThreshold")]
		[Range(0, Int32.MaxValue)]
		public int	ConvergenceThreshold		{ get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptGeneticOptimizerCrossoverRatePercent")]
		[Range(0, 100)]
		public double CrossoverRatePecent
		{
			get { return crossoverRate * 100; }
			set { crossoverRate = Math.Max(0, Math.Min(1, value / 100.0)); }
		}

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptGeneticOptimizerGenerations")]
		[Range(1, Int32.MaxValue)]
		public int		Generations				{ get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptGeneticOptimizerGenerationSize")]
		[Range(1, Int32.MaxValue)]
		public int		GenerationSize			{ get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptGeneticOptimizerMinimumPerformance")]
		[Range(0, Int32.MaxValue)]
		public double	MinimumPerformance		{ get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptGeneticOptimizerMutationRatePercent")]
		[Range(0, 100)]
		public double MutationRatePercent
		{
			get { return mutationRate * 100; }
			set { mutationRate = Math.Max(0, Math.Min(1, value / 100.0)); }
		}

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptGeneticOptimizerMutationStrengthPercent")]
		[Range(0, 100)]
		public double MutationStrengthPercent
		{
			get { return mutationStrength * 100; }
			set { mutationStrength = Math.Max(0, Math.Min(1, value / 100.0)); }
		}

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptGeneticOptimizerResetSizePercent")]
		[Range(0, 100)]
		public double ResetSizePercent
		{
			get { return resetSize * 100; }
			set { resetSize = Math.Max(0, Math.Min(1, value / 100.0)); }
		}

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptGeneticOptimizerStabilitySizePercent")]
		[Range(0, 100)]
		public double StabilitySizePercent
		{
			get { return stabilitySize * 100; }
			set { stabilitySize = Math.Max(0, Math.Min(1, value / 100.0)); }
		}
		#endregion
	}
}
