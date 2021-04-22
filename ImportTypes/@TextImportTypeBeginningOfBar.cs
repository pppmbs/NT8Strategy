// 
// Copyright (C) 2021, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
#endregion


namespace NinjaTrader.NinjaScript.ImportTypes
{
	public class TextImportTypeBeginningOfBar : TextImportType
	{ 
		protected override void OnStateChange()
		{			
			if (State == State.SetDefaults)
			{
				EndOfBarTimestamps	= false;
				Name				= Custom.Resource.ImportTypeNinjaTraderBeginningOfBar;
			}
			else
				base.OnStateChange();
		}
	}
}
