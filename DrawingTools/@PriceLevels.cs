// 
// Copyright (C) 2020, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
#endregion

namespace NinjaTrader.NinjaScript.DrawingTools
{
	// Represents a price level and associated stroke for it
	// used by several drawing tools. Objects must implement ICloneable to 
	// be able to be edited by our collection editor
	[CategoryDefaultExpanded(true)]
	[XmlInclude(typeof(GannAngle))]
	[XmlInclude(typeof(TrendLevel))]
	[TypeConverter("NinjaTrader.NinjaScript.DrawingTools.PriceLevelTypeConverter")]
	public class PriceLevel : NotifyPropertyChangedBase, IStrokeProvider, ICloneable
	{
		private double	value;
		private string	name;

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolsPriceLevelIsVisible", GroupName = "NinjaScriptGeneral")]
		public bool 	IsVisible 	{ get; set; }

		[XmlIgnore]
		[Browsable(false)]
		public bool		IsValueVisible	{ get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolsPriceLevelLineStroke", GroupName = "NinjaScriptGeneral")]
		public Stroke 	Stroke 		{ get; set; }

		[XmlIgnore]
		[Browsable(false)]
		public object Tag { get; set; }

		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolsPriceLevelValue", GroupName = "NinjaScriptGeneral")]
		public double 	Value
		{ 
			get { return value; }
			set
			{
				// Don't clamp to 100, it could go past 100% on eg, time extensions
				this.value = value;
				if (ValueFormatFunc != null)
					Name = ValueFormatFunc(value);
			}
		} 
	
		[XmlIgnore]
		[Browsable(false)]
		public Func<double,string> ValueFormatFunc { get; set; }

		// Name is required to display correctly in our collection editor. This also allows customization between different
		// price level concepts like "100%" versus "1x2" (Gann fan, etc)
		[Browsable(false)]
		public string Name
		{
			get { return name; }
			set
			{
				if (name == value)
					return;
				name = value;
				OnPropertyChanged();
			}
		}

		public virtual object Clone()
		{
			PriceLevel newLvl = new PriceLevel();
			CopyTo(newLvl);
			return newLvl;
		}
		
		public object AssemblyClone(Type t)
		{
			Assembly a 			= t.Assembly;
			object priceLevel 	= a.CreateInstance(t.FullName);
			
			foreach (PropertyInfo p in t.GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
			{
				if (p.CanWrite)
				{
					if (p.PropertyType == typeof(Stroke))
					{
						Stroke copyStroke = new Stroke();
						Stroke.CopyTo(copyStroke);
						p.SetValue(priceLevel, copyStroke, null);
					}
					else
						p.SetValue(priceLevel, this.GetType().GetProperty(p.Name).GetValue(this), null);
				}
			}
			
			return priceLevel;
		}

		public virtual void CopyTo(PriceLevel other)
		{
			other.IsVisible = IsVisible;
			other.IsValueVisible = IsValueVisible;
			other.Name = Name;
			if (Stroke != null)
			{
				other.Stroke = new Stroke();
				Stroke.CopyTo(other.Stroke);
			}
			else 
				other.Stroke = null;
			other.Tag = Tag;
			other.Value = Value;
			other.ValueFormatFunc = ValueFormatFunc;
		}

		public double GetPrice(double startPrice, double totalPriceRange, bool isInverted)
		{
			return isInverted ? startPrice + (1 - Value / 100) * totalPriceRange : startPrice + Value / 100 * totalPriceRange;
		}

		public float GetY(ChartScale chartScale, double startPrice, double totalPriceRange, bool isInverted)
		{
			float yByPrice = chartScale.GetYByValue(GetPrice(startPrice, totalPriceRange, isInverted));
			float pixYAdjust = Math.Abs(yByPrice % 1) > 0.9 ? 0 : 0.5f;
			return yByPrice - pixYAdjust;
		}

		// Parameterless constructor is needed for Clone and serialization
		public PriceLevel() : this (0, Brushes.DimGray, 2f){}
		
		public PriceLevel(double value, Brush brush) : this(value, brush, 2f){}

		public PriceLevel(double value, Brush brush, float strokeWidth) : this (value, brush, strokeWidth, DashStyleHelper.Solid, 100) {}

		public PriceLevel(double value, Brush brush, float strokeWidth, DashStyleHelper dashStyle, int opacity)
		{
			ValueFormatFunc	= v => (v / 100).ToString("P", Core.Globals.GeneralOptions.CurrentCulture);
			Value 			= value;
			IsVisible 		= true;
			Stroke 			= new Stroke(brush, dashStyle, strokeWidth, opacity);
			IsValueVisible	= true;
		}
	}
	
	public class PriceLevelTypeConverter : TypeConverter
	{
		public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext context, object component, Attribute[] attrs)
		{
			PriceLevel priceLevel = component as PriceLevel;
			PropertyDescriptorCollection propertyDescriptorCollection = base.GetPropertiesSupported(context) ?
				base.GetProperties(context, component, attrs) : TypeDescriptor.GetProperties(component, attrs);

			if (priceLevel == null || propertyDescriptorCollection == null)
				return null;

			PropertyDescriptorCollection filtered = new PropertyDescriptorCollection(null);
			foreach (PropertyDescriptor property in propertyDescriptorCollection)
			{
				if ((property.Name != "Value" || priceLevel.IsValueVisible) && property.IsBrowsable)
					filtered.Add(property);
			}

			return filtered;
		}

		public override bool GetPropertiesSupported(ITypeDescriptorContext context)
		{
			return true;
		}
	}

	public abstract class PriceLevelContainer : DrawingTool
	{
		[PropertyEditor("NinjaTrader.Gui.Tools.CollectionEditor")]
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolsPriceLevels", Prompt = "NinjaScriptDrawingToolsPriceLevelsPrompt", GroupName = "NinjaScriptLines", Order = 99)]
		[SkipOnCopyTo(true)]
		public List<PriceLevel> PriceLevels { get; set; }

		public override void CopyTo(NinjaScript ninjaScript)
		{
			base.CopyTo(ninjaScript);

			/* Handle price levels updating.
			 We can't use a cast here, because the incoming NS could be from a newer assembly, 
			 so the cast would always fail. Dig it up using reflection. For the same reason,
			 we need to cast as something without specific type, List<PriceLevel> could fail, because
			 it could try to resovle PriceLevel to current assembly, when its holding a list of PriceLevels
			 from newer assembly as well. For this reason we cast to IList */
			Type			newInstType				= ninjaScript.GetType();
			PropertyInfo	priceLevelPropertyInfo	= newInstType.GetProperty("PriceLevels");
			if (priceLevelPropertyInfo == null)
				return;

			IList newInstPriceLevels = priceLevelPropertyInfo.GetValue(ninjaScript) as IList;
			if (newInstPriceLevels == null)
				return;

			// Since new instance could be past set defaults, clear any existing
			newInstPriceLevels.Clear();
			foreach (PriceLevel oldPriceLevel in PriceLevels)
			{
				try
				{
					// Clone from the new assembly here to prevent losing existing PriceLevels on compile
					object newInstance = oldPriceLevel.AssemblyClone(Core.Globals.AssemblyRegistry.GetType(typeof(PriceLevel).FullName));
					
					if (newInstance == null)
						continue;
					
					newInstPriceLevels.Add(newInstance);
				}
				catch (ArgumentException)
				{
					// In compiled assembly case, Add call will fail for different assemblies so do normal clone instead
					object newInstance = oldPriceLevel.Clone();
					
					if (newInstance == null)
						continue;
					
					// Make sure to update our stroke to a new instance so we dont try to use the old one
					IStrokeProvider strokeProvider = newInstance as IStrokeProvider;
					if (strokeProvider != null)
					{
						Stroke oldStroke = strokeProvider.Stroke;
						strokeProvider.Stroke = new Stroke();
						oldStroke.CopyTo(strokeProvider.Stroke);
					}
					
					newInstPriceLevels.Add(newInstance);
				}
				catch { }
			}
		}

		protected PriceLevelContainer()
		{
			PriceLevels = new List<PriceLevel>();
		}

		public void SetAllPriceLevelsRenderTarget()
		{
			if (PriceLevels == null)
				return;
			foreach (PriceLevel lvl in PriceLevels.Where(pl => pl.Stroke != null))
				lvl.Stroke.RenderTarget = RenderTarget;
		}
	}
}