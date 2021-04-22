// 
// Copyright (C) 2021, NinjaTrader LLC <www.ninjatrader.com>.
// NinjaTrader reserves the right to modify or overwrite this NinjaScript component with each release.
//
#region Using declarations
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using NinjaTrader.Gui.Tools;

#endregion

namespace NinjaTrader.NinjaScript.ShareServices
{
	public class Twitter : ShareService
	{
		private			object icon;
		private const	string oauth_consumer_key	= "jq8MT2wxw93NVhcLrdjg";

		/// <summary>
		/// This MUST be overridden for any custom service properties to be copied over when instances of the service are created
		/// </summary>
		/// <param name="ninjaScript"></param>
		public override void CopyTo(NinjaScript ninjaScript)
		{
			base.CopyTo(ninjaScript);

			// Recompiling NinjaTrader.Custom after a Share service has been added will cause the Type to change.
			//  Use reflection to set the appropriate properties, rather than casting ninjaScript to Twitter.
			PropertyInfo[] props = ninjaScript.GetType().GetProperties();
			foreach (PropertyInfo pi in props)
			{
				if (pi.Name == "LastTimeConfigured")
					pi.SetValue(ninjaScript, LastTimeConfigured);
				else if (pi.Name == "OAuth_Token")
					pi.SetValue(ninjaScript, OAuth_Token);
				else if (pi.Name == "OAuth_Token_Secret")
					pi.SetValue(ninjaScript, OAuth_Token_Secret);
				else if (pi.Name == "UserName")
					pi.SetValue(ninjaScript, UserName);
			}
		}

		public override object Icon
		{
			get
			{
				if (icon == null)
					icon = System.Windows.Application.Current.TryFindResource("ShareIconTwitter");
				return icon;
			}
		}

		private void LogErrorResponse(string result, HttpResponseMessage twitterResponse)
		{
			switch (twitterResponse.StatusCode)
			{
				case HttpStatusCode.BadRequest:
					// If the request is invalid or can't be served, or a request is sent without authorization, you'll get a 400 response
					LogAndPrint(typeof(Custom.Resource), "ShareBadRequestError", new[] { result }, Cbi.LogLevel.Error);
					break;
				case HttpStatusCode.Unauthorized:
					//If Twitter can't or won't authenticate the request, you'll get a 401 response
					LogAndPrint(typeof(Custom.Resource), "ShareNotAuthorized", new[] { result }, Cbi.LogLevel.Error);
					break;
				case HttpStatusCode.Forbidden:
					//If you've gone over your tweet or image limit, you'll get a 403 response
					LogAndPrint(typeof(Custom.Resource), "ShareForbidden", new[] { result }, Cbi.LogLevel.Error);
					break;
				case (HttpStatusCode)429:
					// If you've requested a resource too many times in a short amount of time, you'll get a 429 response
					LogAndPrint(typeof(Custom.Resource), "ShareTooManyRequests", new[] { result }, Cbi.LogLevel.Error);
					break;
				case HttpStatusCode.InternalServerError:
					// If something breaks on Twitter's side, you'll get a 500 response
					LogAndPrint(typeof(Custom.Resource), "ShareInternalServerError", new[] { result }, Cbi.LogLevel.Error);
					break;
				case HttpStatusCode.BadGateway:
					// If Twitter is down or being upgraded, you'll get a 502 response
					LogAndPrint(typeof(Custom.Resource), "ShareBadGatewayError", new[] { result }, Cbi.LogLevel.Error);
					break;
				case HttpStatusCode.ServiceUnavailable:
					// If Twitter's servers are up but overloaded with requests, you'll get a 503 error
					LogAndPrint(typeof(Custom.Resource), "ShareBadGatewayError", new[] { result }, Cbi.LogLevel.Error);
					break;
				case HttpStatusCode.GatewayTimeout:
					// If Twitter's servers are up but the request couldn't be serviced because of some problem on their end, you'll get a 504 response
					LogAndPrint(typeof(Custom.Resource), "ShareGatewayTimeoutError", new[] { result }, Cbi.LogLevel.Error);
					break;
				default:
					LogAndPrint(typeof(Custom.Resource), "ShareNonSuccessCode", new object[] { twitterResponse.StatusCode, result }, Cbi.LogLevel.Error);
					break;
			}
		}

		public override Task OnAuthorizeAccount()
		{
			//Here we go through the OAuth 1.0a sign-in flow
			//	1.) Request a token from Twitter
			//	2.) Have the user authorize NinjaTrader to post on their behalf
			//	3.) Recieve the authorization token that allows us to actually post on their behalf
			#region Twitter Request Token
			string oauth_request_token_url	= "https://api.twitter.com/oauth/request_token";

			string oauth_callback			= "http://www.ninjatrader.com";
			string oauth_timestamp			= Convert.ToInt64((TimeZoneInfo.ConvertTime(Core.Globals.Now, Core.Globals.GeneralOptions.TimeZoneInfo, TimeZoneInfo.Utc) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds, CultureInfo.CurrentCulture).ToString(CultureInfo.CurrentCulture);
			string oauth_nonce				= Convert.ToBase64String(new ASCIIEncoding().GetBytes(Core.Globals.Now.Ticks.ToString()));
			string oauth_signature_method	= "HMAC-SHA1";
			string oauth_version			= "1.0";

			OrderedDictionary sigParameters = new OrderedDictionary
			{
				{ "oauth_callback=",			Core.Globals.UrlEncode(oauth_callback)			+ "&"	},
				{ "oauth_consumer_key=",		Core.Globals.UrlEncode(oauth_consumer_key)		+ "&"	},
				{ "oauth_nonce=",				Core.Globals.UrlEncode(oauth_nonce)				+ "&"	},
				{ "oauth_signature_method=",	Core.Globals.UrlEncode(oauth_signature_method)	+ "&"	},
				{ "oauth_timestamp=",			Core.Globals.UrlEncode(oauth_timestamp)			+ "&"	},
				{ "oauth_version=",				Core.Globals.UrlEncode(oauth_version)					}
			};
			string oauth_signature = Core.Globals.GetTwitterSignature(oauth_request_token_url, "POST", sigParameters);
			
			string header =
				"OAuth" + " " +
				"oauth_callback=\""			+ Core.Globals.UrlEncode(oauth_callback)			+ "\"," +
				"oauth_consumer_key=\""		+ Core.Globals.UrlEncode(oauth_consumer_key)		+ "\"," +
				"oauth_nonce=\""			+ Core.Globals.UrlEncode(oauth_nonce)				+ "\"," +
				"oauth_signature_method=\"" + Core.Globals.UrlEncode(oauth_signature_method)	+ "\"," +
				"oauth_timestamp=\""		+ Core.Globals.UrlEncode(oauth_timestamp)			+ "\"," +
				"oauth_version=\""			+ Core.Globals.UrlEncode(oauth_version)				+ "\"," +
				"oauth_signature=\""		+ Core.Globals.UrlEncode(oauth_signature)			+ "\"";

			string result = string.Empty;
			try
			{
				HttpWebRequest r					= (HttpWebRequest)WebRequest.Create(oauth_request_token_url);
				r.Method							= "POST";
				r.ContentLength						= 0;
				r.ContentType						= "application/x-www-form-urlencoded";
				r.ServicePoint.Expect100Continue	= false;
				r.Headers.Add("Authorization", header);

				using (HttpWebResponse s = (HttpWebResponse) r.GetResponse())
					using (StreamReader reader = new StreamReader(s.GetResponseStream()))
						result = reader.ReadToEnd();
			}
			catch (WebException ex)
			{
				string message = string.Empty;
					using (StreamReader reader = new StreamReader(ex.Response.GetResponseStream()))
						message = reader.ReadToEnd();

				IsConfigured = false;
				SetState(State.Finalized);
				return Task.FromResult(0);
			}

			string oauth_token			= string.Empty;
			string oauth_token_secret	= string.Empty;
			string oauth_verifier		= string.Empty;

			if (!string.IsNullOrEmpty(result))
			{
				string[] pairs = result.Split('&');
				foreach (string pair in pairs)
				{
					string[] keyvalue = pair.Split('=');
					if (keyvalue[0] == "oauth_token")
						oauth_token = keyvalue[1];
					else if (keyvalue[0] == "oauth_token_secret")
						oauth_token_secret = keyvalue[1];
				}
			}			

			#endregion

			#region Twitter Authorize
			//We're going to display a webpage in an NTWindow so the user can authorize our app to post on their behalf.
			//Because of WPF/WinForm airspace issues (see http://msdn.microsoft.com/en-us/library/aa970688.aspx for the gory details), 
			//	and because we want to have our pretty NT-styled windows, we need to finagle things a bit.
			//	1.) Create a modal NTWindow that will pop up when the user clicks "Connect"
			//	2.) Create a borderless window that will actually host the WebBrowser control
			//	3.) A window can have one Content object, so add a grid to the Window hosting the WebBrowser, and make the WeBrowser a child of the grid
			//	4.) Add another grid to the modal NTWindow. We'll use this to place where the WebBrowser goes
			//	5.) Handle the LocationChanged event for the NTWindow and the SizeChanged event for the placement grid. This will take care of making
			//		the hosted WebBrowser control look like it's part of the NTWindow
			//	6.) Make sure the Window hosting the WebBrowser is set to be TopMost so it appears on top of the NTWindow.
			NTWindow authWin = new NTWindow()
				{
					Caption					= Custom.Resource.GuiAuthorize,
					IsModal					= true,
					Height					= 750,
					Width					= 800,
				};

			Window webHost = new Window()
				{
					ResizeMode			= System.Windows.ResizeMode.NoResize,
					ShowInTaskbar		= false,
					WindowStyle			= System.Windows.WindowStyle.None,
				};

			WebBrowser browser = new WebBrowser()
				{
					HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
					VerticalAlignment	= System.Windows.VerticalAlignment.Stretch,					
				};

			Grid grid = new Grid();
			grid.Children.Add(browser);
			webHost.Content = grid;

			Grid placementGrid = new Grid();
			authWin.Content = placementGrid;
			
			authWin.LocationChanged		+= (o, e) => OnSizeLocationChanged(placementGrid, webHost);
			placementGrid.SizeChanged	+= (o, e) => OnSizeLocationChanged(placementGrid, webHost);

			browser.Navigating += (o, e) =>
				{
					if (e.Uri.Host == "www.ninjatrader.com")
					{
						if (e.Uri.Query.StartsWith("?oauth_token"))
						{
							//Successfully authorized! :D
							string query = e.Uri.Query.TrimStart('?');
							string[] pairs = query.Split('&');
							foreach (string pair in pairs)
							{
								string[] keyvalue = pair.Split('=');
								if (keyvalue[0] == "oauth_token")
									oauth_token = keyvalue[1];
								else if (keyvalue[0] == "oauth_verifier")
									oauth_verifier = keyvalue[1];
							}

							authWin.DialogResult = true;
							authWin.Close();
						}
						else if (e.Uri.Query.StartsWith("?denied"))
						{
							//User denied authorization :'(
							authWin.DialogResult = false;
							authWin.Close();
						}
					}
				};
			authWin.Closing += (o, e) => webHost.Close();

			browser.Navigate(new Uri("https://api.twitter.com/oauth/authorize?oauth_token=" + oauth_token));
			webHost.Visibility	= System.Windows.Visibility.Visible;
			webHost.Topmost		= true;
			authWin.ShowDialog();

			#endregion

			#region Twitter Access Token
			if (authWin.DialogResult == true)
			{
				string oauth_access_token_url = "https://api.twitter.com/oauth/access_token";

				oauth_timestamp = Convert.ToInt64((TimeZoneInfo.ConvertTime(Core.Globals.Now, Core.Globals.GeneralOptions.TimeZoneInfo, TimeZoneInfo.Utc) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds, CultureInfo.CurrentCulture).ToString(CultureInfo.CurrentCulture);
				oauth_nonce		= Convert.ToBase64String(new ASCIIEncoding().GetBytes(Core.Globals.Now.Ticks.ToString()));

				sigParameters.Clear();
				sigParameters.Add("oauth_consumer_key=",		Core.Globals.UrlEncode(oauth_consumer_key) + "&");
				sigParameters.Add("oauth_nonce=",				Core.Globals.UrlEncode(oauth_nonce) + "&");
				sigParameters.Add("oauth_signature_method=",	Core.Globals.UrlEncode(oauth_signature_method) + "&");
				sigParameters.Add("oauth_timestamp=",			Core.Globals.UrlEncode(oauth_timestamp) + "&");
				sigParameters.Add("oauth_token=",				Core.Globals.UrlEncode(oauth_token) + "&");
				sigParameters.Add("oauth_verifier=",			Core.Globals.UrlEncode(oauth_verifier) + "&");
				sigParameters.Add("oauth_version=",				Core.Globals.UrlEncode(oauth_version));

				oauth_signature = Core.Globals.GetTwitterSignature(oauth_access_token_url, "POST", sigParameters);

				header =
					"OAuth" + " " +
					"oauth_consumer_key=\""		+ Core.Globals.UrlEncode(oauth_consumer_key) + "\"," +
					"oauth_nonce=\""			+ Core.Globals.UrlEncode(oauth_nonce) + "\"," +
					"oauth_signature_method=\"" + Core.Globals.UrlEncode(oauth_signature_method) + "\"," +
					"oauth_timestamp=\""		+ Core.Globals.UrlEncode(oauth_timestamp) + "\"," +
					"oauth_token=\""			+ Core.Globals.UrlEncode(oauth_token) + "\"," +
					"oauth_verifier=\""			+ Core.Globals.UrlEncode(oauth_verifier) + "\"," +
					"oauth_version=\""			+ Core.Globals.UrlEncode(oauth_version) + "\"," +
					"oauth_signature=\""		+ Core.Globals.UrlEncode(oauth_signature) + "\"";

				try
				{
					HttpWebRequest r					= (HttpWebRequest)WebRequest.Create(oauth_access_token_url + "?oauth_verifier=" + Core.Globals.UrlEncode(oauth_verifier));
					r.Method							= "POST";
					r.ContentLength						= 0;
					r.ContentType						= "application/x-www-form-urlencoded";
					r.ServicePoint.Expect100Continue	= false;
					r.Headers.Add("Authorization", header);

					using (HttpWebResponse s = (HttpWebResponse)r.GetResponse())
					using (StreamReader reader = new StreamReader(s.GetResponseStream()))
						result = reader.ReadToEnd();

					if (!string.IsNullOrEmpty(result))
					{
						string[] pairs = result.Split('&');
						foreach (string pair in pairs)
						{
							string[] keyvalue = pair.Split('=');
							if (keyvalue[0] == "oauth_token")
								OAuth_Token = keyvalue[1];
							else if (keyvalue[0] == "oauth_token_secret")
								OAuth_Token_Secret = keyvalue[1];
							else if (keyvalue[0] == "screen_name")
								UserName = keyvalue[1];
						}
					}
				}
				catch (WebException ex)
				{
					string message = string.Empty;
					using (StreamReader reader = new StreamReader(ex.Response.GetResponseStream()))
						message = reader.ReadToEnd();

					IsConfigured = false;
					SetState(State.Finalized);
					return Task.FromResult(0);
				}

				IsConfigured = !string.IsNullOrEmpty(OAuth_Token) && !string.IsNullOrEmpty(OAuth_Token_Secret) && !string.IsNullOrEmpty(UserName);
			}
			else
				IsConfigured = false;
			#endregion

			return Task.FromResult(0);

		}

		public async override Task OnShare(string text, string imageFilePath)
		{
			if (State != State.Active)
				throw new InvalidOperationException("Not a valid state to perform this action. State=" + State);

			//https://dev.twitter.com/docs/counting-characters
			string tweet = text.Normalize();

			if (string.IsNullOrEmpty(imageFilePath))
			{
				string			twitter_update_url		= "https://api.twitter.com/1.1/statuses/update.json";

				string			oauth_timestamp			= Convert.ToInt64((TimeZoneInfo.ConvertTime(Core.Globals.Now, Core.Globals.GeneralOptions.TimeZoneInfo, TimeZoneInfo.Utc) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds, CultureInfo.CurrentCulture).ToString(CultureInfo.CurrentCulture);
				string			oauth_nonce				= Convert.ToBase64String(new ASCIIEncoding().GetBytes(Core.Globals.Now.Ticks.ToString()));
				string			oauth_signature_method	= "HMAC-SHA1";
				string			oauth_version			= "1.0";

				OrderedDictionary sigParameters			= new OrderedDictionary
				{
					{ "oauth_consumer_key=",		Core.Globals.UrlEncode(oauth_consumer_key)		+ "&"	},
					{ "oauth_nonce=",				Core.Globals.UrlEncode(oauth_nonce)				+ "&"	},
					{ "oauth_signature_method=",	Core.Globals.UrlEncode(oauth_signature_method)	+ "&"	},
					{ "oauth_timestamp=",			Core.Globals.UrlEncode(oauth_timestamp)			+ "&"	},
					{ "oauth_token=",				Core.Globals.UrlEncode(OAuth_Token)				+ "&"	},
					{ "oauth_version=",				Core.Globals.UrlEncode(oauth_version)			+ "&"	},
					{ "status=",					Core.Globals.UrlEncode(tweet)							}
				};
				string oauth_signature = Core.Globals.GetTwitterSignature(twitter_update_url, "POST", OAuth_Token_Secret, sigParameters);

				string header =
					"oauth_consumer_key=\""		+ Core.Globals.UrlEncode(oauth_consumer_key)		+ "\"," +
					"oauth_nonce=\""			+ Core.Globals.UrlEncode(oauth_nonce)				+ "\"," +
					"oauth_signature_method=\"" + Core.Globals.UrlEncode(oauth_signature_method)	+ "\"," +
					"oauth_timestamp=\""		+ Core.Globals.UrlEncode(oauth_timestamp)			+ "\"," +
					"oauth_token=\""			+ Core.Globals.UrlEncode(OAuth_Token)				+ "\","	+
					"oauth_version=\""			+ Core.Globals.UrlEncode(oauth_version)				+ "\"," +
					"oauth_signature=\""		+ Core.Globals.UrlEncode(oauth_signature)			+ "\"";

				try
				{
					using (HttpClient client = new HttpClient())
					{
						string				postData				= "status=" + Core.Globals.UrlEncode(tweet);
						byte[]				encodedPostData			= new ASCIIEncoding().GetBytes(postData);
						HttpContent			byteContent				= new ByteArrayContent(encodedPostData);

						byteContent.Headers.ContentType				= new MediaTypeHeaderValue("application/x-www-form-urlencoded");
						client.DefaultRequestHeaders.Authorization	= new AuthenticationHeaderValue("OAuth", header);
						client.DefaultRequestHeaders.ExpectContinue = false;

						HttpResponseMessage twitterResponse			= await client.PostAsync(twitter_update_url, byteContent);

						string result = new StreamReader(twitterResponse.Content.ReadAsStreamAsync().Result).ReadToEnd();
						// true if StatusCode was in the range 200-299; otherwise false.
						if (!twitterResponse.IsSuccessStatusCode)
							LogErrorResponse(result, twitterResponse);
						else
							LogAndPrint(typeof(Custom.Resource), "ShareTwitterSentSuccessfully", new[] { Name }, Cbi.LogLevel.Information);
					}
				}
				catch (WebException ex)
				{
					string message = string.Empty;
					using (StreamReader reader = new StreamReader(ex.Response.GetResponseStream()))
						message = reader.ReadToEnd();

					SetState(State.Finalized);
					return;
				}
			}
			else
			{
				string twitter_update_with_media_url	= "https://api.twitter.com/1.1/statuses/update_with_media.json";

				if (!File.Exists(imageFilePath))
				{
					LogAndPrint(typeof(Custom.Resource), "ShareImageNoLongerExists", new[] { imageFilePath }, Cbi.LogLevel.Error);
					SetState(State.Finalized);
					return;
				}

				byte[]						imageBytes				= File.ReadAllBytes(imageFilePath);
				string						oauth_timestamp			= Convert.ToInt64((TimeZoneInfo.ConvertTime(Core.Globals.Now, Core.Globals.GeneralOptions.TimeZoneInfo, TimeZoneInfo.Utc) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds, CultureInfo.CurrentCulture).ToString(CultureInfo.CurrentCulture);
				string						oauth_nonce				= Convert.ToBase64String(new ASCIIEncoding().GetBytes(Core.Globals.Now.Ticks.ToString()));
				string						oauth_signature_method	= "HMAC-SHA1";
				string						oauth_version			= "1.0";
				OrderedDictionary			sigParameters			= new OrderedDictionary
				{
					{ "oauth_consumer_key=",		Core.Globals.UrlEncode(oauth_consumer_key)		+ "&"	},
					{ "oauth_nonce=",				Core.Globals.UrlEncode(oauth_nonce)				+ "&"	},
					{ "oauth_signature_method=",	Core.Globals.UrlEncode(oauth_signature_method)	+ "&"	},
					{ "oauth_timestamp=",			Core.Globals.UrlEncode(oauth_timestamp)			+ "&"	},
					{ "oauth_token=",				Core.Globals.UrlEncode(OAuth_Token)				+ "&"	},
					{ "oauth_version=",				Core.Globals.UrlEncode(oauth_version)					}
				};
				string oauth_signature = Core.Globals.GetTwitterSignature(twitter_update_with_media_url, "POST", OAuth_Token_Secret, sigParameters);

				string header =
					"oauth_consumer_key=\""		+ Core.Globals.UrlEncode(oauth_consumer_key)		+ "\"," +
					"oauth_nonce=\""			+ Core.Globals.UrlEncode(oauth_nonce)				+ "\"," +
					"oauth_signature_method=\"" + Core.Globals.UrlEncode(oauth_signature_method)	+ "\"," +
					"oauth_timestamp=\""		+ Core.Globals.UrlEncode(oauth_timestamp)			+ "\"," +
					"oauth_token=\""			+ Core.Globals.UrlEncode(OAuth_Token)				+ "\","	+
					"oauth_version=\""			+ Core.Globals.UrlEncode(oauth_version)				+ "\"," +
					"oauth_signature=\""		+ Core.Globals.UrlEncode(oauth_signature)			+ "\"";

				string result = string.Empty;
				try
				{
					HttpContent tweetContent	= new StringContent(tweet);
					HttpContent imageContent	= new ByteArrayContent(imageBytes);
					using (HttpClient client = new HttpClient())
						using (MultipartFormDataContent formData = new MultipartFormDataContent())
						{
							formData.Add(tweetContent, "status");
							formData.Add(imageContent, "media");

							client.DefaultRequestHeaders.Connection.Add("Keep-Alive");
							client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("OAuth", header);
							client.DefaultRequestHeaders.ExpectContinue = false;
							HttpResponseMessage twitterResponse = await client.PostAsync(twitter_update_with_media_url, formData);

							result = new StreamReader(twitterResponse.Content.ReadAsStreamAsync().Result).ReadToEnd();
							// true if StatusCode was in the range 200-299; otherwise false.
							if (!twitterResponse.IsSuccessStatusCode)
								LogErrorResponse(result, twitterResponse);
							else
								LogAndPrint(typeof(Custom.Resource), "ShareTwitterSentSuccessfully", new[] { Name }, Cbi.LogLevel.Information);
						}
				}
				catch (WebException ex)
				{
					//Like to result from a timeout
					string message = string.Empty;
					using (StreamReader reader = new StreamReader(ex.Response.GetResponseStream()))
						message = reader.ReadToEnd();

					LogAndPrint(typeof(Custom.Resource), "ShareWebException", new object[] { ex.Status, ex.Message }, Cbi.LogLevel.Error);
				}
				catch(Exception ex)
				{
					LogAndPrint(typeof(Custom.Resource), "ShareServiceSignature", new[] { ex.Message }, Cbi.LogLevel.Error);
				}
			}
		}

		private static void OnSizeLocationChanged(System.Windows.FrameworkElement placementTarget, System.Windows.Window webHost)
		{
			//Here we set the location and size of the borderless Window hosting the WebBrowser control. 
			//	This is based on the location and size of the child grid of the NTWindow. When the grid changes,
			//	the hosted WebBrowser changes to match.
			if (webHost.Visibility == System.Windows.Visibility.Visible)
				webHost.Show();

			webHost.Owner							= Window.GetWindow(placementTarget);
			Point				locationFromScreen	= placementTarget.PointToScreen(new System.Windows.Point(0, 0));
			PresentationSource	source				= PresentationSource.FromVisual(webHost);
			if (source != null && source.CompositionTarget != null)
			{
				Point targetPoints	= source.CompositionTarget.TransformFromDevice.Transform(locationFromScreen);
				webHost.Left		= targetPoints.X;
				webHost.Top			= targetPoints.Y;
			}

			webHost.Width	= placementTarget.ActualWidth;
			webHost.Height	= placementTarget.ActualHeight;
		}

		protected async override void OnStateChange()
		{			
			if (State == State.SetDefaults)
			{
				CharacterLimit				= 280;
				CharactersReservedPerMedia	= int.MaxValue;
				IsConfigured				= false;
				IsDefault					= false;
				IsImageAttachmentSupported	= true;
				Name						= Custom.Resource.TwitterServiceName;
				Signature					= Custom.Resource.TwitterSignature;
				UseOAuth					= true;
				UserName					= string.Empty;
			}
			else if (State == State.Active)
			{
				//Here we grab some configuration information once a day from twitter, just URL character lengths right now.
				// Twitter only wants this checked once per day, so save it and check that unless it's been 24 hours since the last update
				// to the config information. 
				XElement	config	= Core.Globals.Config.Element(Core.Globals.ProductName).Element("TwitterConfig");
				if (config != null)
				{
					DateTime lastConfigTime;
					if (config.Element("LastConfigured") != null)
					{
						if (DateTime.TryParse(config.Element("LastConfigured").Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out lastConfigTime))
						{
							LastTimeConfigured = lastConfigTime;
							int charsReserved;

							if (config.Element("CharsReserved") != null && int.TryParse(config.Element("CharsReserved").Value, out charsReserved))
								CharactersReservedPerMedia = charsReserved;
						}
					}
				}

				bool needsConfigUpdate = IsConfigured 
										&& (CharactersReservedPerMedia == int.MaxValue 
											|| LastTimeConfigured == DateTime.MinValue 
											|| (Core.Globals.Now - LastTimeConfigured).Hours > 24);

				if (needsConfigUpdate)
				{
					string						oauth_timestamp				= Convert.ToInt64((TimeZoneInfo.ConvertTime(Core.Globals.Now, Core.Globals.GeneralOptions.TimeZoneInfo, TimeZoneInfo.Utc) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds, CultureInfo.CurrentCulture).ToString(CultureInfo.CurrentCulture);
					string						oauth_nonce					= Convert.ToBase64String(new ASCIIEncoding().GetBytes(Core.Globals.Now.Ticks.ToString()));
					string						oauth_signature_method		= "HMAC-SHA1";
					string						oauth_version				= "1.0";
					string						twitter_configuration_url	= "https://api.twitter.com/1.1/help/configuration.json";

					OrderedDictionary			sigParameters				= new OrderedDictionary
					{
						{ "oauth_consumer_key=",		Core.Globals.UrlEncode(oauth_consumer_key)		+ "&"	},
						{ "oauth_nonce=",				Core.Globals.UrlEncode(oauth_nonce)				+ "&"	},
						{ "oauth_signature_method=",	Core.Globals.UrlEncode(oauth_signature_method)	+ "&"	},
						{ "oauth_timestamp=",			Core.Globals.UrlEncode(oauth_timestamp)			+ "&"	},
						{ "oauth_token=",				Core.Globals.UrlEncode(OAuth_Token)				+ "&"	},
						{ "oauth_version=",				Core.Globals.UrlEncode(oauth_version)					}
					};
					string oauth_signature = Core.Globals.GetTwitterSignature(twitter_configuration_url, "GET", OAuth_Token_Secret, sigParameters);

					string header =
						"oauth_consumer_key=\""		+ Core.Globals.UrlEncode(oauth_consumer_key)		+ "\"," +
						"oauth_nonce=\""			+ Core.Globals.UrlEncode(oauth_nonce)				+ "\"," +
						"oauth_signature_method=\"" + Core.Globals.UrlEncode(oauth_signature_method)	+ "\"," +
						"oauth_timestamp=\""		+ Core.Globals.UrlEncode(oauth_timestamp)			+ "\"," +
						"oauth_token=\""			+ Core.Globals.UrlEncode(OAuth_Token)				+ "\","	+
						"oauth_version=\""			+ Core.Globals.UrlEncode(oauth_version)				+ "\"," +
						"oauth_signature=\""		+ Core.Globals.UrlEncode(oauth_signature)			+ "\"";

					string result = string.Empty;

					try
					{
						using (HttpClient client = new HttpClient())
						{
							client.DefaultRequestHeaders.Connection.Add("Keep-Alive");
							client.DefaultRequestHeaders.Authorization	= new AuthenticationHeaderValue("OAuth", header);
							client.DefaultRequestHeaders.ExpectContinue = false;
							HttpResponseMessage twitterResponse			= await client.GetAsync(twitter_configuration_url);

							result = new StreamReader(twitterResponse.Content.ReadAsStreamAsync().Result).ReadToEnd();
							// true if StatusCode was in the range 200-299; otherwise false.
							if (!twitterResponse.IsSuccessStatusCode)
							{
								LogErrorResponse(result, twitterResponse);
								return;
							}

							result = result.Trim(new char[] {'{', '}'});
							string[] configResults = result.Split(',');
							foreach (string configItem in configResults)
							{
								string[] keyValue = configItem.Split(':');
								if (keyValue[0] == "\"characters_reserved_per_media\"")
								{
									CharactersReservedPerMedia	= int.Parse(keyValue[1]);
									LastTimeConfigured			= Core.Globals.Now;
									if (config == null)
										Core.Globals.Config.Element(Core.Globals.ProductName).Add(config = new XElement("TwitterConfig"));
									XElement charsReserved = config.Element("CharsReserved");
									if (charsReserved == null)
										config.Add(charsReserved = new XElement("CharsReserved"));
									charsReserved.Value = CharactersReservedPerMedia.ToString(CultureInfo.InvariantCulture);
									XElement lastConfigured = config.Element("LastConfigured");
									if (lastConfigured == null)
										config.Add(lastConfigured = new XElement("LastConfigured"));
									lastConfigured.Value = Core.Globals.Now.ToString(CultureInfo.InvariantCulture);
									break;
								}
							}
						}
					}
					catch (WebException ex)
					{
						//Like to result from a timeout
						string message = string.Empty;
						using (StreamReader reader = new StreamReader(ex.Response.GetResponseStream()))
							message = reader.ReadToEnd();

						LogAndPrint(typeof(Custom.Resource), "ShareWebException", new object[] { ex.Status, ex.Message }, Cbi.LogLevel.Error);
					}
					catch(Exception ex)
					{
						LogAndPrint(typeof(Custom.Resource), "ShareServiceSignature", new[] { ex.Message }, Cbi.LogLevel.Error);
					}
				}
			}
		}

		#region Properties
		[Browsable(false)]
		public DateTime LastTimeConfigured { get; set; }

		[Gui.Encrypt]
		[Browsable(false)]
		public string OAuth_Token { get; set; }

		[Gui.Encrypt]
		[Browsable(false)]
		public string OAuth_Token_Secret { get; set; }

		[ReadOnly(true)]
		[Display(ResourceType = typeof(Custom.Resource), Name = "ShareServiceUserName", GroupName = "ShareServiceParameters", Order = 1)]
		public string UserName
		{ get; set; }
		#endregion
	}
}
