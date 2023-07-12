// 
// Copyright (C) 2022, NinjaTrader LLC <www.ninjatrader.com>.
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
				if (pi.Name == "OAuth_Token")
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

		public override async Task OnAuthorizeAccount()
		{
			//Here we go through the OAuth 1.0a sign-in flow
			//	1.) Request a token from Twitter
			//	2.) Have the user authorize NinjaTrader to post on their behalf
			//	3.) Recieve the authorization token that allows us to actually post on their behalf
			#region Twitter Request Token
			string oauth_request_token_url	= "https://api.twitter.com/oauth/request_token";

			string oauth_callback			= "http://127.0.0.1:2943";
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
				return;
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
			// Open the user's default browser to the auth page
			System.Diagnostics.Process.Start("https://api.twitter.com/oauth/authorize?oauth_token=" + oauth_token);
			string authString = string.Empty;

			// Listen for the redirect to localhost indicating successful authorization
			using (HttpListener listener = new HttpListener())
			{
				listener.Prefixes.Add(oauth_callback + "/");
				listener.Start();

				HttpListenerContext		context			= await listener.GetContextAsync();
				HttpListenerRequest		request			= context.Request;
				authString								= request.RawUrl;

				// We create and display a styled HTML page once successful redirect has occurred
				HttpListenerResponse	response		= context.Response;
				
				string html = @"<!DOCTYPE html>
								<html class=""no-js"" style=""height:100%"">
									<head>
										<meta http-equiv=""Content-Type"" content=""text/html; charset=UTF-8"">
										<meta http-equiv=""X-UA-Compatible"" content=""IE=edge"">
										<title>NinjaTrader</title>
										<meta name=""description"" content="""">
										<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
										<style type=""text/css"">
								a,body,div,footer,h1,header,html,img,p,span,sup{{margin:0;padding:0;border:0;font:inherit;font-size:100%;vertical-align:baseline}}html{{line-height:1}}footer,header{{display:block}}*{{-moz-box-sizing:border-box;-webkit-box-sizing:border-box;box-sizing:border-box}}:focus{{outline:0;border:none}}a:active,a:focus{{border:0;outline:0}}body{{background-color:#fff;overflow-x:hidden}}sup{{vertical-align:text-top;font-size:70%}}h1{{font-size:50px;font-size:3.125rem;line-height:50px;line-height:3.125rem;color:#4d4d4d;text-align:center;font-family:ProximaNova-Bold,Helvetica,Arial,sans-serif!important;margin-bottom:16px;margin-bottom:1rem}}@media (min-width:300px) and (max-width:600px){{h1{{font-size:40px;font-size:2.5rem;line-height:40px;line-height:2.5rem;color:#4d4d4d;text-align:center;font-family:ProximaNova-Bold,Helvetica,Arial,sans-serif!important;margin-bottom:16px;margin-bottom:1rem}}}}p{{font-size:24px;font-size:1.5rem;line-height:32px;line-height:2rem;color:#4d4d4d;text-align:center;font-family:ProximaNova-Regular,Helvetica,Arial,sans-serif!important;margin-bottom:30px;margin-bottom:1.875rem}}@media (min-width:300px) and (max-width:600px){{p{{font-size:16px;font-size:1rem;line-height:18px;line-height:1.125rem;color:#4d4d4d;text-align:center;font-family:ProximaNova-Regular,Helvetica,Arial,sans-serif!important;margin-bottom:8px;margin-bottom:.5rem}}}}@media (min-width:300px) and (max-width:600px){{p{{margin-bottom:1rem}}}}a{{font-size:24px;font-size:1.5rem;line-height:32px;line-height:2rem;color:#4d4d4d;text-align:center;font-family:ProximaNova-Regular,Helvetica,Arial,sans-serif!important;text-decoration:none;-webkit-transition:all 350ms ease;-moz-transition:all 350ms ease;-ms-transition:all 350ms ease;-o-transition:all 350ms ease;transition:all 350ms ease}}@media (min-width:300px) and (max-width:600px){{a{{font-size:16px;line-height:18px}}}}.t-left{{text-align:left}}.t-base{{font-size:16px!important;line-height:20px!important}}.c-red{{color:#a41e23}}.b-black{{background-color:#231f20!important}}img.inline{{max-height:100%;max-width:100%;vertical-align:bottom}}@media (min-width:300px) and (max-width:600px){{img.inline{{max-width:90%;max-height:100%;height:auto;vertical-align:bottom;margin:0 5%}}}}.l-row{{width:100%}}.l-block{{max-width:71.3em;padding-left:1em;padding-right:1em;margin-left:auto;margin-right:auto;padding:30px;padding:1.875rem;height:100%}}.l-block:after{{content:"""";display:table;clear:both}}@media (min-width:300px) and (max-width:600px){{.l-block{{padding:.75rem}}}}.l-four{{width:32.39832%;float:left;margin-right:1.40252%;display:inline}}@media (min-width:300px) and (max-width:600px){{.l-four{{width:100%;float:left;margin-right:1.40252%;display:inline;margin-right:0}}}}.l-twelve{{width:100%;float:left;margin-right:1.40252%;display:inline;margin-right:0}}@media (min-width:300px) and (max-width:600px){{.l-twelve{{width:100%;float:left;margin-right:1.40252%;display:inline;margin-right:0}}}}.l-nmt{{margin-top:0!important}}.l-nmb{{margin-bottom:0!important}}.l-mb1{{margin-bottom:16px!important}}.l-np{{padding:0}}.l-nav{{padding:25px 0;padding:1.5625rem 0}}@media (min-width:300px) and (max-width:600px){{.l-nav{{padding:8px 0;padding:.5rem 0}}}}footer{{width:100%;float:left;margin-right:1.40252%;display:inline;margin-right:0}}footer .l-row{{background:#6e6c6d url(data:image/webp;base64,UklGRi4AAABXRUJQVlA4TCEAAAAvCcAOAA9wF/jPwj8mfv7jAQQCFOH/ZgM6ENH/CUD9mQAA) left top repeat-x}}footer .l-block{{padding:96px 0 16px 0;padding:6rem 0 1rem 0}}@media (min-width:300px) and (max-width:600px){{footer .l-block{{padding:96px 16px 16px 16px;padding:6rem 1rem 1rem 1rem}}}}@media (min-width:601px) and (max-width:1024px){{footer .l-block{{padding:96px 16px 16px 16px;padding:6rem 1rem 1rem 1rem}}}}footer .l-block p{{font-size:16px;font-size:1rem;line-height:20px;line-height:1.25rem;color:#d0d2d3;text-align:left;font-family:ProximaNova-Regular,Helvetica,Arial,sans-serif!important}}@media (min-width:300px) and (max-width:600px){{footer .l-block{{padding:6rem 1rem}}}}#l-nav-block{{-js-display:flex;display:flex;align-items:center;height:30px}}@media (min-width:601px) and (max-width:1024px){{#l-nav-block{{display:block;height:auto}}}}@media (min-width:300px) and (max-width:600px){{#l-nav-block{{display:block;padding-bottom:.5rem;height:auto}}}}body{{-webkit-backface-visibility:hidden}}body{{display:flex;flex-direction:column;min-height:100vh}}footer{{margin:auto auto 0 auto}}
										</style>
									</head>
									<body>
										<header>
											<div class=""l-row b-black"">
												<div id=""l-nav-block"" class=""l-block l-np"">
													&nbsp;
												</div>
											</div>
											<div class=""l-row white"">
												<div class=""l-block l-nav"">
													<span class=""l-four"">
														<img src=""data:image/webp;base64,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"" alt=""NinjaTrader"" class=""inline"">
													</span>
												</div>
											</div>
										</header>
										<div class=""l-row content"">
											<div class=""l-block"">
												<h1 class=""l-twelve t-left"">{0}</h1>
												<p class=""l-twelve l-mb1 t-base t-left"">{1}</p>
												<p class=""l-twelve l-mb1 t-base t-left"">{2}</p>
											</div>
										</div>
										<footer>
											<div class=""l-row"">
												<div class=""l-block"" style=""padding-top:5rem!important;"">
													<p class=""l-twelve l-mb1 l-nmt"">{3}</p>
													<p id=""disc"" class=""l-twelve l-nmb"">{4}</p>
												</div>
											</div>
										</footer>
									</body>
								</html>";

				string		authorizeHeader	= Custom.Resource.TwitterAuthHeader;
				string		authorizeText	= string.Format(Custom.Resource.TwitterAuthText1, Core.Globals.ProductName);
				string		authorizeText2	= string.Format(Custom.Resource.TwitterAuthText2, Core.Globals.ProductName);
				string		disclosure1		= string.Format(CultureInfo.InvariantCulture, Custom.Resource.AuthDisclosureText1, Core.Globals.Now.Year);
				string		disclosure2		= Custom.Resource.AuthDisclosureText2;
				string		responseString	= string.Format(html, authorizeHeader, authorizeText, authorizeText2, disclosure1, disclosure2);
				
				byte[]		buffer			= System.Text.Encoding.UTF8.GetBytes(responseString);
				response.ContentLength64	= buffer.Length;
				using (System.IO.Stream output = response.OutputStream)
					await output.WriteAsync(buffer, 0, buffer.Length); // This actually writes the HTML to the local host for the user to see
				listener.Close();
			}

			bool successfullyAuthorized = false;
			if (!string.IsNullOrEmpty(authString))
			{
				if (authString.StartsWith("/?oauth_token"))
				{
					//Successfully authorized! :D
					string query = authString.TrimStart('/', '?');
					string[] pairs = query.Split('&');
					foreach (string pair in pairs)
					{
						string[] keyvalue = pair.Split('=');
						if (keyvalue[0] == "oauth_token")
							oauth_token = keyvalue[1];
						else if (keyvalue[0] == "oauth_verifier")
							oauth_verifier = keyvalue[1];
					}

					successfullyAuthorized = true;
				}
			}
			#endregion

			#region Twitter Access Token
			if (successfullyAuthorized)
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
					return;
				}

				IsConfigured = !string.IsNullOrEmpty(OAuth_Token) && !string.IsNullOrEmpty(OAuth_Token_Secret) && !string.IsNullOrEmpty(UserName);
			}
			else
				IsConfigured = false;
			#endregion

			return;

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

		protected override void OnStateChange()
		{			
			if (State == State.SetDefaults)
			{
				CharacterLimit				= 280;
				CharactersReservedPerMedia	= 0;
				IsConfigured				= false;
				IsDefault					= false;
				IsImageAttachmentSupported	= true;
				Name						= Custom.Resource.TwitterServiceName;
				Signature					= Custom.Resource.TwitterSignature;
				UseOAuth					= true;
				UserName					= string.Empty;
			}
		}

		#region Properties
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
