// 
// Copyright (C) 2021, NinjaTrader LLC <www.ninjatrader.com>.
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
using System.Xml.Linq;
using System.Xml.Serialization;
#endregion

namespace NinjaTrader.NinjaScript.Optimizers
{
	public class DefaultOptimizer : Optimizer
	{
		private int[] enumIndexes;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Name								= NinjaTrader.Custom.Resource.NinjaScriptOptimizerDefault;
				SupportsMultiObjectiveOptimization	= true;
			}
			else if (State == State.Configure && Strategies.Count > 0)
			{
				enumIndexes			= new int[Strategies[0].OptimizationParameters.Count];
				NumberOfIterations	= GetParametersCombinationsCount(Strategies[0]);
			}
		}

		protected override void OnOptimize()
		{
			Iterate(0);
		}

		/// <summary>
		/// This methods iterates the parameters recursively. The actual back test is performed as the last parameter is iterated.
		/// </summary>
		/// <param name="index"></param>
		private void Iterate(int index)
		{
			if (Strategies[0].OptimizationParameters.Count == 0)
				return;

			Parameter parameter = Strategies[0].OptimizationParameters[index];
			for (int i = 0; ; i++)
			{
				if (IsAborted)
					return;
				
				if (parameter.ParameterType == typeof(int))
				{
					if ((int) parameter.Min + i * parameter.Increment > (int) parameter.Max + parameter.Increment / 1000000)
						return;
					parameter.Value = (int) parameter.Min + i * parameter.Increment;
				}
				else if (parameter.ParameterType == typeof(double))
				{
					if ((double) parameter.Min + i * parameter.Increment > (double) parameter.Max + parameter.Increment / 1000000)
						return;
					parameter.Value = (double) parameter.Min + i * parameter.Increment;
				}
				else if (parameter.ParameterType == typeof(bool))
				{
					if (i == 0)			
						parameter.Value = parameter.Min;
					else if (i == 1 && (bool) parameter.Min != (bool) parameter.Max)
						parameter.Value = !(bool) parameter.Value;
					else
						return;
				}
				else if (parameter.ParameterType.IsEnum)
				{
					if (enumIndexes[index] >= parameter.EnumValues.Length)
					{
						enumIndexes[index] = 0;
 						return;
					}
					else
						parameter.Value = parameter.EnumValues[enumIndexes[index]++];
				}

				if (index == Strategies[0].OptimizationParameters.Count - 1)	// Is this the Last parameter -> run the iteration
					RunIteration();												// 
				else															// Iterate next parameter in line
					Iterate(index + 1);
			}
		}
	}
}
