using System;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("NinjaTrader Custom")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("NinjaTrader")]
[assembly: AssemblyProduct("NinjaTrader")]
[assembly: AssemblyCopyright("NinjaTrader© 2003-2019 NinjaTrader LLC")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: CLSCompliant(true)]
[assembly: ComVisible(false)]

#if PRODUCTION
[assembly: AssemblyVersion("8.0.21.1")]
#else
[assembly: AssemblyVersion("8.0.22.0")]
#endif

[assembly: NeutralResourcesLanguage("en", UltimateResourceFallbackLocation.MainAssembly)]
