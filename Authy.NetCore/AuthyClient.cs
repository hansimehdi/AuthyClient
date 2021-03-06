﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Authy.netcore.Results;
using Newtonsoft.Json;

namespace Authy.netcore
{
    /// <summary>
    /// Client for interacting with the Authy API
    /// </summary>
    /// <remarks>
    /// This library is threadsafe since the only shared state is stored in private readonly fields.
    ///
    /// Creating a single instance of the client and using it across multiple threads isn't a problem.
    /// </remarks>
    public class AuthyClient
    {
        private readonly string _apiKey;
        private readonly bool _test;

        /// <summary>
        /// Creates an instance of the Authy client
        /// </summary>
        /// <param name="apiKey">The api key used to access the rest api</param>
        /// <param name="test">indicates that the sandbox should be used</param>
        public AuthyClient(string apiKey, bool test = false)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new Exception("Api key is missing");
            }

            _apiKey = apiKey;
            _test = test;
        }

        private string BaseUrl => _test ? "http://sandbox-api.authy.com" : "https://api.authy.com";

        /// <summary>
        /// Register a user
        /// </summary>
        /// <param name="email">Email address</param>
        /// <param name="cellPhoneNumber">Cell phone number</param>
        /// <param name="countryCode">Country code</param>
        /// <returns>RegisterUserResult object containing the details about the attempted register user request</returns>
        public RegisterUserResult RegisterUser(string email, string cellPhoneNumber, int countryCode = 1)
        {
            var request = new System.Collections.Specialized.NameValueCollection()
            {
                {"user[email]", email},
                {"user[cellphone]", cellPhoneNumber},
                {"user[country_code]", countryCode.ToString()}
            };

            var url = $"{BaseUrl}/protected/json/users/new?api_key={_apiKey}";
            return Execute(client =>
            {
                var response = client.UploadValues(url, request);
                var textResponse = Encoding.ASCII.GetString(response);

                var apiResponse = JsonConvert.DeserializeObject<RegisterUserResult>(textResponse);
                apiResponse.RawResponse = textResponse;
                apiResponse.Status = AuthyStatus.Success;
                apiResponse.UserId = apiResponse.User["id"];

                return apiResponse;
            });
        }
        
        
        /// <summary>
        /// Remove a user
        /// <param name="id">User id</param>
        /// </summary>
        public RemoveUserResult RemoveUser(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new Exception("User id is missing");
            }
            
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new Exception("Invalid user id");
            }
            var request = new System.Collections.Specialized.NameValueCollection();

            var url = $"{BaseUrl}/protected/json/users/{id}/remove?api_key={_apiKey}&force=true";
            return Execute(client =>
            {
                var response = client.UploadValues(url, request);
                var textResponse = Encoding.ASCII.GetString(response);

                var apiResponse = JsonConvert.DeserializeObject<RemoveUserResult>(textResponse);
                apiResponse.RawResponse = textResponse;
                apiResponse.Status = AuthyStatus.Success;
                apiResponse.Message = apiResponse.Message;

                return apiResponse;
            });
        }

        /// <summary>
        /// Verify a token with authy
        /// </summary>
        /// <param name="userId">The Authy user id</param>
        /// <param name="token">The token to verify</param>
        /// <param name="force">Force verification to occur even if the user isn't registered (if the user hasn't finished registering the deefault is to succesfully validate)</param>
        public VerifyTokenResult VerifyToken(string userId, string token, bool force = false)
        {
            if (!AuthyHelpers.TokenIsValid(token))
            {
                var errors = new Dictionary<string, string> {{"token", "is invalid"}};

                return new VerifyTokenResult()
                {
                    Status = AuthyStatus.BadRequest,
                    Success = false,
                    Message = "Token is invalid.",
                    Errors = errors
                };
            }

            token = AuthyHelpers.SanitizeNumber(token);
            userId = AuthyHelpers.SanitizeNumber(userId);

            var url =
                $"{BaseUrl}/protected/json/verify/{token}/{userId}?api_key={_apiKey}{(force ? "&force=true" : string.Empty)}";
            return Execute(client =>
            {
                var response = client.DownloadString(url);

                var apiResponse = JsonConvert.DeserializeObject<VerifyTokenResult>(response);

                if (apiResponse.Token == "is valid")
                {
                    apiResponse.Status = AuthyStatus.Success;
                }
                else
                {
                    apiResponse.Success = false;
                    apiResponse.Status = AuthyStatus.Unauthorized;
                }

                apiResponse.RawResponse = response;

                return apiResponse;
            });
        }

        /// <summary>
        /// Send an SMS message to a user who isn't registered.  If the user is registered with a mobile app then no message will be sent.
        /// </summary>
        /// <param name="userId">The user ID to send the message to</param>
        /// <param name="force">Force a message to be sent even if the user is already reigistered as an app user.  This will incrase your costs</param>
        /// <param name="locale"></param>
        public SendSmsResult SendSms(string userId, bool force = false,string locale = "en")
        {
            userId = AuthyHelpers.SanitizeNumber(userId);

            var url =
                $"{BaseUrl}/protected/json/sms/{userId}?api_key={_apiKey}{(force ? "&force=true" : string.Empty)}&locale={locale}";
            return Execute(client =>
            {
                var response = client.DownloadString(url);

                var apiResponse = JsonConvert.DeserializeObject<SendSmsResult>(response);
                apiResponse.Status = AuthyStatus.Success;
                apiResponse.RawResponse = response;

                return apiResponse;
            });
        }


        /// <summary>
        /// Send the token via phone call to a user who isn't registered.  If the user is registered with a mobile app then the phone call will be ignored.
        /// </summary>
        /// <param name="userId">The user ID to send the phone call to</param>
        /// <param name="force">Force to the phone call to be sent even if the user is already reigistered as an app user.  This will incrase your costs</param>
        public AuthyResult StartPhoneCall(string userId, bool force = false)
        {
            userId = AuthyHelpers.SanitizeNumber(userId);

            var url =
                $"{BaseUrl}/protected/json/call/{userId}?api_key={_apiKey}{(force ? "&force=true" : string.Empty)}";
            return Execute(client =>
            {
                var response = client.DownloadString(url);

                var apiResponse = JsonConvert.DeserializeObject<AuthyResult>(response);
                apiResponse.Status = AuthyStatus.Success;
                apiResponse.RawResponse = response;

                return apiResponse;
            });
        }

        private static TResult Execute<TResult>(Func<WebClient, TResult> execute)
            where TResult : AuthyResult, new()
        {
            var client = new WebClient();
            var libraryVersion = AuthyHelpers.GetVersion();
            var runtimeVersion = AuthyHelpers.GetSystemInfo();
            var userAgent = $"AuthyNet/{libraryVersion} ({runtimeVersion})";

            // Set a custom user agent
            client.Headers.Add("user-agent", userAgent);

            try
            {
                return execute(client);
            }
            catch (WebException webex)
            {
                var response = webex.Response.GetResponseStream();

                string body;
                using (var reader = new StreamReader(response ?? throw new Exception("Error streaming response")))
                {
                    body = reader.ReadToEnd();
                }

                var result = JsonConvert.DeserializeObject<TResult>(body);

                switch (((HttpWebResponse) webex.Response).StatusCode)
                {
                    case HttpStatusCode.ServiceUnavailable:
                        result.Status = AuthyStatus.ServiceUnavailable;
                        break;
                    case HttpStatusCode.Unauthorized:
                        result.Status = AuthyStatus.Unauthorized;
                        break;
                    default:
                    case HttpStatusCode.BadRequest:
                        result.Status = AuthyStatus.BadRequest;
                        break;
                }

                return result;
            }
            finally
            {
                client.Dispose();
            }
        }
    }
}