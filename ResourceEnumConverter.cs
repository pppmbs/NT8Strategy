using System;
namespace NinjaTrader.Custom
{
	internal class ResourceEnumConverter : Infralution.Localization.Wpf.ResourceEnumConverter
	{
		public ResourceEnumConverter(Type type) : base(type, Resource.ResourceManager) {  }
	}
}