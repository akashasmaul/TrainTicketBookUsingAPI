using RestSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Web;
using TrainTicketBookUsingAPI;
using OpenQA.Selenium.DevTools.V127.WebAuthn;
using System.Security.Cryptography.X509Certificates;

public class TrainBookingAPI
{
    private RestClient client;
    private bool formSubmitted = false;
    private string baseUrl = "https://railspaapi.shohoz.com/v1.0/web";
    private string token;
    private const string refererUrl = "https://eticket.railway.gov.bd/";
    private const string LocalServerUrl = "http://localhost:5000/";

    public string CollectedOtp { get; internal set; }

    public TrainBookingAPI()
    {
        client = new RestClient(baseUrl);
    }

    // Authenticate user and obtain token
    public string Authenticate(string mobileNumber, string password)
    {
        var request = new RestRequest("/auth/sign-in", Method.Post);
        request.AddJsonBody(new { mobile_number = mobileNumber, password = password });

        var response = client.Execute(request);

        if (response.IsSuccessful)
        {
            dynamic authResponse = JsonConvert.DeserializeObject(response.Content);
            token = authResponse?.token ?? authResponse?.access_token ?? authResponse?.data?.token;

            if (string.IsNullOrEmpty(token))
            {
                throw new Exception("Token is missing in the response.");
            }

            return token;
        }
        else
        {
            dynamic errorResponse = JsonConvert.DeserializeObject(response.Content);
            string errorMsg = errorResponse?.error?.message ?? "Unknown error occurred during authentication.";
            throw new Exception($"Authentication failed: {errorMsg}");
        }
    }

    // Search for available trips
    public List<TrainTrip> SearchTrips(string fromCity, string toCity, string dateOfJourney, string seatClass, string tripNumber = null)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new Exception("No token found. Please authenticate first.");
        }

        var request = new RestRequest($"/bookings/search-trips-v2?from_city={fromCity}&to_city={toCity}&date_of_journey={dateOfJourney}&seat_class={seatClass}", Method.Get);
        request.AddHeader("Authorization", "Bearer " + token);

        var response = client.Execute(request);

        // Log the full response for debugging
        Console.WriteLine("Response Status Code: " + response.StatusCode);
        //Console.WriteLine("Response Content: " + response.Content);

        if (response.IsSuccessful)
        {
            dynamic trips = JsonConvert.DeserializeObject(response.Content);
            List<TrainTrip> availableTrips = new List<TrainTrip>();

            if (trips?.data != null)
            {
                foreach (var trip in trips.data.trains)
                {
                    if (!string.IsNullOrEmpty(tripNumber) && !trip.trip_number.Equals(tripNumber, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var availableTrip = new TrainTrip
                    {
                        TripNumber = trip.trip_number,
                        DepartureDateTime = trip.departure_date_time,
                        SeatClasses = new List<SeatClass>(),
                        BoardingPoints = new List<BoardingPoint>() // Initialize the list for boarding points
                    };

                    foreach (var seatType in trip.seat_types)
                    {
                        if (seatType.type == seatClass && seatType.seat_counts.online > 0)
                        {
                            availableTrip.SeatClasses.Add(new SeatClass
                            {
                                SeatType = seatType.type,
                                TripId = seatType.trip_id,
                                TripRouteId = seatType.trip_route_id,
                                RouteId = seatType.route_id
                            });
                        }
                    }

                    // Parse boarding points
                    if (trip.boarding_points != null)
                    {
                        foreach (var boardingPoint in trip.boarding_points)
                        {
                            availableTrip.BoardingPoints.Add(new BoardingPoint
                            {
                                TripPointId = boardingPoint.trip_point_id,
                                LocationId = boardingPoint.location_id,
                                LocationName = boardingPoint.location_name,
                                LocationTime = boardingPoint.location_time,
                                LocationDate = boardingPoint.location_date
                            });
                        }
                    }

                    if (availableTrip.SeatClasses.Count > 0)
                    {
                        availableTrips.Add(availableTrip);
                    }
                }
            }
            else
            {
                Console.WriteLine("No trips found in response.");
                throw new Exception("No trips found.");
            }

            return availableTrips;
        }
        else
        {
            Console.WriteLine("Error Response Content: " + response.Content);
            dynamic errorResponse = JsonConvert.DeserializeObject(response.Content);
            string errorMsg = errorResponse?.error?.message ?? "Error occurred while fetching trips.";
            throw new Exception($"Error fetching trips: {errorMsg}");
        }
    }

    // Get available seats for a specific trip
    public List<Seat> GetAvailableSeats(string tripId, string tripRouteId)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new Exception("No token found. Please authenticate first.");
        }

        var request = new RestRequest($"/bookings/seat-layout?trip_id={tripId}&trip_route_id={tripRouteId}", Method.Get);
        request.AddHeader("Authorization", "Bearer " + token);

        var response = client.Execute(request);

        Console.WriteLine("Response Status Code: " + response.StatusCode);
        //Console.WriteLine("Full Response: " + response.Content);

        if (response.IsSuccessful)
        {
            List<Seat> availableSeats = new List<Seat>();

            JObject seatLayout = JObject.Parse(response.Content);
            var seatLayouts = seatLayout["data"]?["seatLayout"];

            if (seatLayouts != null && seatLayouts.Type == JTokenType.Array)
            {
                foreach (var seatGroup in seatLayouts)
                {
                    var layoutArray = seatGroup["layout"];

                    if (layoutArray != null && layoutArray.Type == JTokenType.Array)
                    {
                        foreach (var row in layoutArray)
                        {
                            if (row.Type == JTokenType.Array)
                            {
                                foreach (var seatData in row)
                                {
                                    ProcessSeatData(seatData, availableSeats);
                                }
                            }
                        }
                    }
                }
            }

            return availableSeats;
        }
        else
        {
            dynamic errorResponse = JsonConvert.DeserializeObject(response.Content);
            string errorMsg = errorResponse?.error?.message ?? "Error occurred while fetching seat layout.";
            throw new Exception($"Error fetching seat layout: {errorMsg}");
        }
    }

    // Process seat data and filter available seats
    private void ProcessSeatData(JToken seatData, List<Seat> availableSeats)
    {
        var seatNumber = seatData["seat_number"]?.ToString();
        var seatAvailability = seatData["seat_availability"];
        var ticketId = seatData["ticket_id"]?.ToString();

        if (seatNumber != null && seatAvailability != null && seatAvailability.Type == JTokenType.Integer)
        {
            int availability = seatAvailability.ToObject<int>();

            if (availability == 1)
            {
                availableSeats.Add(new Seat
                {
                    SeatNumber = seatNumber,
                    SeatAvailability = availability,
                    TicketId = ticketId
                });

                Console.WriteLine($"Seat {seatNumber} is available and added.");
            }
            else
            {
                Console.WriteLine($"Seat {seatNumber} is not available.");
            }
        }
    }

    // Method to book seats with a previously generated ticket ID
    public void BookSingleSeat(string seatNumber, string tripId, string routeId, string ticketId)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new Exception("No token found. Please authenticate first.");
        }

        Console.WriteLine($"Booking single seat with details: Trip ID: {tripId}, Route ID: {routeId}, Seat: {seatNumber}, Ticket ID: {ticketId}");

        var request = new RestRequest("/bookings/reserve-seat", Method.Patch);
        request.AddHeader("Authorization", "Bearer " + token);

        // Prepare the request payload for booking a single ticket
        var body = new
        {
            ticket_id = Convert.ToInt32(ticketId), // Ensure this is sent as an integer
            route_id = routeId
        };

        Console.WriteLine("Request Payload:");
        Console.WriteLine(JsonConvert.SerializeObject(body, Formatting.Indented));

        request.AddJsonBody(body);
        var response = client.Execute(request);

        if (!response.IsSuccessful)
        {
            Console.WriteLine($"HTTP request failed with status: {response.StatusCode}");
            Console.WriteLine("Response content: " + response.Content);
            throw new Exception("Error booking seat: " + response.Content);
        }
        else
        {
            JObject bookingResponse = JObject.Parse(response.Content);
            var error = bookingResponse["data"]?["error"]?.ToObject<int>();
            var message = bookingResponse["data"]?["message"]?.ToString();

            if (error == 0)
            {
                Console.WriteLine($"Booking successful for seat {seatNumber}: {message}");
            }
            else
            {
                Console.WriteLine($"Booking failed for seat {seatNumber} with message: " + message);
                throw new Exception("Error booking seat: " + message);
            }
        }
    }

    public void BookSeatsSequentially(List<string> seatNumbers, string tripId, string routeId, List<string> ticketIds)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new Exception("No token found. Please authenticate first.");
        }

        Console.WriteLine("Starting to book seats sequentially:");

        foreach (var ticketId in ticketIds)
        {
            // Assuming each seat number corresponds to each ticket ID
            var seatNumber = seatNumbers[ticketIds.IndexOf(ticketId)]; // Get the corresponding seat number

            try
            {
                // Call the method to book a single seat
                BookSingleSeat(seatNumber, tripId, routeId, ticketId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error booking ticket ID {ticketId} for seat {seatNumber}: {ex.Message}");
                // You may choose to continue booking or break based on your requirement
                // continue; // Uncomment if you want to continue booking even if one fails
                break; // Exit on error, if desired
            }
        }
    }

    public void ContinuePurchase(string tripId, string tripRouteId, List<string> ticketIds)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new Exception("No token found. Please authenticate first.");
        }

        var request = new RestRequest("/bookings/passenger-details", Method.Post);
        request.AddHeader("Authorization", "Bearer " + token);

        var body = new
        {
            trip_id = tripId,
            trip_route_id = tripRouteId,
            ticket_ids = ticketIds
        };

        Console.WriteLine("Verification Request Payload:");
        Console.WriteLine(JsonConvert.SerializeObject(body, Formatting.Indented));

        request.AddJsonBody(body);
        var response = client.Execute(request);

        if (!response.IsSuccessful)
        {
            Console.WriteLine($"Verification request failed with status: {response.StatusCode}");
            Console.WriteLine("Response content: " + response.Content);
            throw new Exception("Error verifying booking: " + response.Content);
        }
        else
        {
            Console.WriteLine("OTP Sent. Input OTP...");
            StartLocalServer();
        }
    }

    public void StartLocalServer()
    {
        HttpListener listener = new HttpListener();
        listener.Prefixes.Add(LocalServerUrl);
        listener.Start();
        Console.WriteLine("Listening on " + LocalServerUrl);

        // Open browser automatically to the local server
        Process.Start(new ProcessStartInfo
        {
            FileName = LocalServerUrl,
            UseShellExecute = true
        });
        Task.Run(() =>
        {
            while (listener.IsListening && !formSubmitted)
            {
                try
                {
                    var context = listener.GetContext();
                    var request = context.Request;
                    var response = context.Response;

                    if (request.HttpMethod == "POST")
                    {
                        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                        {
                            string requestData = reader.ReadToEnd();

                            // Parse the POST data to extract only the value of "otp"
                            string otpValue = null;
                            var postParams = requestData.Split('&');
                            foreach (var param in postParams)
                            {
                                var keyValue = param.Split('=');
                                if (keyValue.Length == 2 && keyValue[0] == "otp")
                                {
                                    otpValue = keyValue[1];
                                    break;
                                }
                            }

                            // Print the extracted value
                            if (!string.IsNullOrEmpty(otpValue))
                            {
                                Console.WriteLine("Received POST Data: " + otpValue);
                                CollectedOtp = otpValue;
                            }
                            else
                            {
                                Console.WriteLine("No 'otp' value found in the POST data.");
                            }

                            // Mark form as submitted
                            formSubmitted = true;

                            // Send response to confirm data was received
                            byte[] buffer = Encoding.UTF8.GetBytes("Data received and processed. You can close this page.");
                            response.ContentLength64 = buffer.Length;
                            response.OutputStream.Write(buffer, 0, buffer.Length);
                        }
                    }
                    else // Serve HTML form for GET requests
                    {
                        string html = "<html><body>" +
                                      "<h2>Enter OTP and Submit</h2>" +
                                      "<form method='post' action='http://localhost:5000/'>" +
                                      "<label for='otp'>OTP:</label>" +
                                      "<input type='text' id='otp' name='otp'>" +
                                      "<input type='submit' value='Submit'>" +
                                      "</form>" +
                                      "</body></html>";

                        byte[] buffer = Encoding.UTF8.GetBytes(html);
                        response.ContentLength64 = buffer.Length;
                        response.OutputStream.Write(buffer, 0, buffer.Length);
                    }

                    // Close the response
                    response.OutputStream.Close();
                }
                catch (HttpListenerException ex)
                {
                    Console.WriteLine("Listener stopped: " + ex.Message);
                    break;
                }
            }

            // Stop the listener after form submission
            listener.Stop();
            Console.WriteLine("Server stopped.");
        });
    }

    private void CaptureOtpFromPostData(string postData)
    {
        // Parse the POST data to extract OTP value
        var data = HttpUtility.ParseQueryString(postData);
        CollectedOtp = data["otp"]; // Capture the OTP value from the form data

        Console.WriteLine($"Captured OTP: {CollectedOtp}");

        // Optionally, you could add validation here to check if OTP is valid
    }

    public void VerifyOtp(string tripId, string tripRouteId, List<string> ticketIds, string otp)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new Exception("No token found. Please authenticate first.");
        }

        var apiRequest = new RestRequest("/bookings/verify-otp", Method.Post);
        apiRequest.AddHeader("Authorization", "Bearer " + token);

        var body = new
        {
            trip_id = tripId,
            trip_route_id = tripRouteId,
            ticket_ids = ticketIds,
            otp = otp
        };

        apiRequest.AddJsonBody(body);

        var response = client.Execute(apiRequest);

        if (!response.IsSuccessful)
        {
            // Log detailed error information
            Console.WriteLine($"Verification request failed with status: {response.StatusCode}");
            Console.WriteLine("Response content: " + response.Content);
            Console.WriteLine("Request Payload:");
            Console.WriteLine(JsonConvert.SerializeObject(body, Formatting.Indented));

            // Check for server error specifically
            if (response.StatusCode == HttpStatusCode.InternalServerError)
            {
                Console.WriteLine("Server-side error occurred.");
                Console.WriteLine("Hash for debugging: " + JsonConvert.DeserializeObject<dynamic>(response.Content)?.extra?.hash);
            }

            throw new Exception("Error verifying booking: " + response.Content);
        }
        else
        {
            Console.WriteLine("OTP verification successful.");
        }
    }

    public void ConfirmBooking(string tripId, string tripRouteId, List<string> ticketIds, string otp)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new Exception("No token found. Please authenticate first.");
        }

        var apiRequest = new RestRequest("/bookings/confirm", Method.Patch);
        apiRequest.AddHeader("Authorization", "Bearer " + token);

        if (Credentials.ticketNumber == 1)
        {
            var body = new
            {
                trip_id = tripId,
                trip_route_id = tripRouteId,
                ticket_ids = ticketIds,
                boarding_point_id = Credentials.boardingPointId,
                contactperson = 0,
                passengerType = new[] { "Adult" },
                pemail = Credentials.email,
                pmobile = Credentials.MobileNumber,
                pname = new[] { Credentials.name },
                gender = new[] { "male" },
                selected_mobile_transaction = "3",
                otp = otp
            };

            apiRequest.AddJsonBody(body);

            var response = client.Execute(apiRequest);
            Console.WriteLine("=== Response Details ===");
            Console.WriteLine($"Status Code: {response.StatusCode}");
            Console.WriteLine($"Response Content: {response.Content}");

            if (response.StatusCode == HttpStatusCode.OK)
            {
                // Parse the response content to extract the redirectUrl
                var jsonResponse = JObject.Parse(response.Content);
                var redirectUrl = jsonResponse["data"]?["redirectUrl"]?.ToString();

                if (!string.IsNullOrEmpty(redirectUrl))
                {
                    // Normalize the URL if necessary (if it's escaped or formatted improperly)
                    var normalizedUrl = Uri.UnescapeDataString(redirectUrl);

                    // Open the URL in the default web browser
                    OpenUrlInBrowser(normalizedUrl);
                }
                else
                {
                    Console.WriteLine("Redirect URL not found in response.");
                }
            }
            else
            {
                Console.WriteLine("Booking confirmation failed: " + response.Content);
                throw new Exception("Booking confirmation failed.");
            }

            if (!response.IsSuccessful)
            {
                Console.WriteLine($"Booking confirmation request failed with status: {response.StatusCode}");
                Console.WriteLine("Response content: " + response.Content);
                Console.WriteLine("Request Payload:");
                Console.WriteLine(JsonConvert.SerializeObject(body, Formatting.Indented));
                throw new Exception("Error confirming booking: " + response.Content);
            }
            else
            {
                Console.WriteLine("Booking confirmed successfully.");
            }
        }
        else if (Credentials.ticketNumber == 2)
        {
            var body = new
            {
                trip_id = tripId,
                trip_route_id = tripRouteId,
                ticket_ids = ticketIds,
                boarding_point_id = Credentials.boardingPointId,
                contactperson = 0,
                passengerType = new[] { "Adult", "Adult" },
                pemail = Credentials.email,
                pmobile = Credentials.MobileNumber,
                pname = new[] { Credentials.name, Credentials.partnerName },
                gender = new[] { "male", "male" },
                selected_mobile_transaction = "3",
                otp = otp
            };

            apiRequest.AddJsonBody(body);

            var response = client.Execute(apiRequest);
            Console.WriteLine("=== Response Details ===");
            Console.WriteLine($"Status Code: {response.StatusCode}");
            Console.WriteLine($"Response Content: {response.Content}");

            if (response.StatusCode == HttpStatusCode.OK)
            {
                // Parse the response content to extract the redirectUrl
                var jsonResponse = JObject.Parse(response.Content);
                var redirectUrl = jsonResponse["data"]?["redirectUrl"]?.ToString();

                if (!string.IsNullOrEmpty(redirectUrl))
                {
                    // Normalize the URL if necessary (if it's escaped or formatted improperly)
                    var normalizedUrl = Uri.UnescapeDataString(redirectUrl);

                    // Open the URL in the default web browser
                    OpenUrlInBrowser(normalizedUrl);
                }
                else
                {
                    Console.WriteLine("Redirect URL not found in response.");
                }
            }
            else
            {
                Console.WriteLine("Booking confirmation failed: " + response.Content);
                throw new Exception("Booking confirmation failed.");
            }

            if (!response.IsSuccessful)
            {
                Console.WriteLine($"Booking confirmation request failed with status: {response.StatusCode}");
                Console.WriteLine("Response content: " + response.Content);
                Console.WriteLine("Request Payload:");
                Console.WriteLine(JsonConvert.SerializeObject(body, Formatting.Indented));
                throw new Exception("Error confirming booking: " + response.Content);
            }
            else
            {
                Console.WriteLine("Booking confirmed successfully.");
            }
        }
        else
        {
            var body = new
            {
                trip_id = tripId,
                trip_route_id = tripRouteId,
                ticket_ids = ticketIds,
                boarding_point_id = Credentials.boardingPointId,
                contactperson = 0,
                passengerType = new[] { "Adult" },
                pemail = Credentials.email,
                pmobile = Credentials.MobileNumber,
                pname = new[] { Credentials.name },
                gender = new[] { "male" },
                selected_mobile_transaction = "3",
                otp = otp
            };

            apiRequest.AddJsonBody(body);

            var response = client.Execute(apiRequest);
            Console.WriteLine("=== Response Details ===");
            Console.WriteLine($"Status Code: {response.StatusCode}");
            Console.WriteLine($"Response Content: {response.Content}");

            if (response.StatusCode == HttpStatusCode.OK)
            {
                // Parse the response content to extract the redirectUrl
                var jsonResponse = JObject.Parse(response.Content);
                var redirectUrl = jsonResponse["data"]?["redirectUrl"]?.ToString();

                if (!string.IsNullOrEmpty(redirectUrl))
                {
                    // Normalize the URL if necessary (if it's escaped or formatted improperly)
                    var normalizedUrl = Uri.UnescapeDataString(redirectUrl);

                    // Open the URL in the default web browser
                    OpenUrlInBrowser(normalizedUrl);
                }
                else
                {
                    Console.WriteLine("Redirect URL not found in response.");
                }
            }
            else
            {
                Console.WriteLine("Booking confirmation failed: " + response.Content);
                throw new Exception("Booking confirmation failed.");
            }

            if (!response.IsSuccessful)
            {
                Console.WriteLine($"Booking confirmation request failed with status: {response.StatusCode}");
                Console.WriteLine("Response content: " + response.Content);
                Console.WriteLine("Request Payload:");
                Console.WriteLine(JsonConvert.SerializeObject(body, Formatting.Indented));
                throw new Exception("Error confirming booking: " + response.Content);
            }
            else
            {
                Console.WriteLine("Booking confirmed successfully.");
            }
        }
    }

    private void OpenUrlInBrowser(string url)
    {
        try
        {
            // Open the URL in the default web browser
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            Console.WriteLine("Redirecting to: " + url);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to open the URL in browser: " + ex.Message);
        }
    }

    public void SetOtp(string otp)
    {
        CollectedOtp = otp;
        Console.WriteLine("OTP set successfully: " + CollectedOtp); // Confirm OTP is set
    }

    public void WaitForOtp(int maxRetries = 5, int delaySeconds = 5)
    {
        int retries = maxRetries;
        while (string.IsNullOrEmpty(CollectedOtp) && retries > 0)
        {
            Thread.Sleep(delaySeconds * 1000);
            retries--;

            if (!string.IsNullOrEmpty(CollectedOtp))
            {
                Console.WriteLine("OTP received successfully within retries.");
                break;
            }
            else
            {
                Console.WriteLine($"Retrying... Attempts left: {retries}");
            }
        }

        if (string.IsNullOrEmpty(CollectedOtp))
        {
            Console.WriteLine("Failed to collect OTP after all retries.");
            throw new Exception("OTP collection failed.");
        }
    }
}

// TrainTrip, SeatClass, and Seat classes

public class TrainTrip
{
    public string TripNumber { get; set; }
    public string DepartureDateTime { get; set; }
    public List<SeatClass> SeatClasses { get; set; }
    public List<BoardingPoint> BoardingPoints { get; set; }
}

public class BoardingPoint
{
    public int TripPointId { get; set; }
    public int LocationId { get; set; }
    public string LocationName { get; set; }
    public string LocationTime { get; set; }
    public string LocationDate { get; set; }
}

public class SeatClass
{
    public string SeatType { get; set; }
    public int TripId { get; set; }
    public int TripRouteId { get; set; }
    public int RouteId { get; set; }
}

public class Seat
{
    public string SeatNumber { get; set; }
    public int SeatAvailability { get; set; }
    public string TicketId { get; set; } // Optional: Store the ticket ID if it exists
}

//public class ApiResponse
//{
//    public Data Data { get; set; }
//    public Extra Extra { get; set; }
//}

//public class Data
//{
//    public string Message { get; set; }
//    public string RedirectUrl { get; set; }
//}

//public class Extra
//{
//    public string Hash { get; set; }
//}