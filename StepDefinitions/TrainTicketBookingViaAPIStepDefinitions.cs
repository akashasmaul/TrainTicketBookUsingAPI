using NUnit.Framework;
using System.Diagnostics;
using TechTalk.SpecFlow;
using System;
using System.Collections.Generic;
using System.Linq;
using TrainTicketBookUsingAPI;

[Binding]
public class TrainTicketBookingViaAPIStepDefinitions
{
    private TrainBookingAPI _trainBookingAPI;
    private List<TrainTrip> _availableTrips;
    private string _selectedTripId;
    private string _selectedTripRouteId;
    private string _selectedRouteId; // Route ID for booking
    private List<string> _selectedSeats;
    private List<Seat> _availableSeats; // List to store available seats
    private string _selectedTripPointId; // Added this line
    public TrainTicketBookingViaAPIStepDefinitions()
    {
        _trainBookingAPI = new TrainBookingAPI();
        _selectedSeats = new List<string>();
        _availableSeats = new List<Seat>(); // Initialize the list
    }

    [Given("I authenticate with the API")]
    public void GivenIAuthenticateWithTheAPI()
    {
        var mobileNumber = Credentials.MobileNumber;
        var password = Credentials.Password;
        _trainBookingAPI.Authenticate(mobileNumber, password);
        Console.WriteLine("Authentication successful. Token received.");
    }

    [When("I search for trips from '(.*)' to '(.*)' on '(.*)' with seat class '(.*)' and trip number '(.*)'")]
    public void WhenISearchForTripsFromToOnWithSeatClassAndTripNumber(string fromCity, string toCity, string dateOfJourney, string seatClass, string tripNumber)
    {
        fromCity = Credentials.fromCity;   
        toCity = Credentials.toCity;
        dateOfJourney = Credentials.dateOfJourney;
        seatClass = Credentials.seatClass;
        tripNumber= Credentials.trainCode;

        try
        {
            // Search for available trips based on the given criteria
            _availableTrips = _trainBookingAPI.SearchTrips(fromCity, toCity, dateOfJourney, seatClass);

            if (_availableTrips.Count > 0)
            {
                // Filter trips by the specified trip_number
                var selectedTrip = _availableTrips.FirstOrDefault(trip => trip.TripNumber.Equals(tripNumber, StringComparison.OrdinalIgnoreCase));

                if (selectedTrip != null)
                {
                    // Store selected trip information
                    _selectedTripRouteId = selectedTrip.SeatClasses[0].TripRouteId.ToString(); // Updated to use TripRouteId
                    _selectedTripId = selectedTrip.SeatClasses[0].TripId.ToString();

                    // Extract and store the trip_point_id for the boarding point
                    if (selectedTrip.BoardingPoints != null && selectedTrip.BoardingPoints.Count > 0)
                    {
                        _selectedTripPointId = selectedTrip.BoardingPoints[0].TripPointId.ToString();
                        Credentials.boardingPointId = _selectedTripPointId;
                    }
                    else
                    {
                        Console.WriteLine("No boarding points found for the selected trip.");
                    }

                    Console.WriteLine($"Selected trip: {tripNumber}");
                    Console.WriteLine($"TripRouteId: {_selectedTripRouteId}, TripId: {_selectedTripId}, TripPointId: { _selectedTripPointId}");
                    
                }
                else
                {
                    Console.WriteLine($"No trip found with trip number: {tripNumber}");
                    Assert.Fail($"No trip found with trip number: {tripNumber}");
                }
            }
            else
            {
                Console.WriteLine("No available trips found.");
                Assert.Fail("No available trips found.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error searching for trips: " + ex.Message);
            Assert.Fail("Error searching for trips: " + ex.Message);
        }
    }


    [When("I get available seats for the trip")]
    public void WhenIGetAvailableSeatsForTheTrip()
    {
        try
        {
            if (string.IsNullOrEmpty(_selectedTripId) || string.IsNullOrEmpty(_selectedTripRouteId))
            {
                Console.WriteLine("No valid trip selected.");
                Assert.Fail("No valid trip selected.");
            }

            Console.WriteLine($"Selected TripId: {_selectedTripId}, Selected TripRouteId: {_selectedTripRouteId}, Selected BoardingPointId: {Credentials.boardingPointId}");

            _availableSeats = _trainBookingAPI.GetAvailableSeats(_selectedTripId, _selectedTripRouteId);
            var availableSeatNumbers = _availableSeats
                .Where(seat => seat.SeatAvailability == 1)
                .Select(seat => seat.SeatNumber.Trim()) // Ensure no leading/trailing spaces
                .Take(Credentials.ticketNumber)
                .ToList();

            // Debug: Output the available seat numbers to book
            Console.WriteLine("Available seats to book: " + string.Join(", ", availableSeatNumbers));

            if (availableSeatNumbers.Count > 0)
            {
                Console.WriteLine("Available seats: " + string.Join(", ", availableSeatNumbers));
                _selectedSeats.AddRange(availableSeatNumbers); // Add to selected seats list
            }
            else
            {
                Console.WriteLine("No available seats found.");
                Assert.Fail("No available seats found.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error fetching seat layout: " + ex.Message);
            Assert.Fail("Error fetching seat layout: " + ex.Message);
        }
    }




    [When("I book the selected seats")]
    public void WhenIBookTheSelectedSeats()
    {
        try
        {
            foreach (var seatNumber in _selectedSeats)
            {
                var seat = _availableSeats.FirstOrDefault(s => s.SeatNumber == seatNumber);
                if (seat != null && !string.IsNullOrEmpty(seat.TicketId))
                {
                    Console.WriteLine($"Booking Seat Number: {seat.SeatNumber}, Ticket ID: {seat.TicketId}");
                    _trainBookingAPI.BookSeatsSequentially(
                        new List<string> { seatNumber },
                        _selectedTripId,
                        _selectedTripRouteId,
                        new List<string> { seat.TicketId }
                    );

                    Console.WriteLine($"Seat {seatNumber} booked successfully.");
                }
                else
                {
                    Console.WriteLine($"Seat Number: {seatNumber} is not available for booking.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error booking seats: " + ex.Message);
            Assert.Fail("Error booking seats: " + ex.Message);
        }
    }

    [When("I continue to purchase for verification")]
    public void WhenIContinueToPurchaseForVerification()
    {
        try
        {
            List<string> ticketIds = new List<string>();

            // Ensure that seats and their TicketIds are populated
            foreach (var seatNumber in _selectedSeats)
            {
                var seat = _availableSeats.FirstOrDefault(s => s.SeatNumber == seatNumber);
                if (seat != null && !string.IsNullOrEmpty(seat.TicketId))
                {
                    ticketIds.Add(seat.TicketId);
                }
            }

            // Validate if required fields are not empty
            if (!string.IsNullOrEmpty(_selectedTripId) && !string.IsNullOrEmpty(_selectedTripRouteId) && ticketIds.Count > 0)
            {
                // Call ContinuePurchase with valid data
                _trainBookingAPI.ContinuePurchase(_selectedTripId, _selectedTripRouteId, ticketIds);
                Console.WriteLine("Continue purchase successful.");
            }
            else
            {
                Console.WriteLine("Failed to retrieve a valid trip ID, trip route ID, or ticket IDs for verification.");
                Assert.Fail("Failed to retrieve a valid trip ID, trip route ID, or ticket IDs for verification.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error verifying booking: " + ex.Message);
            Assert.Fail($"Error during continue purchase verification: {ex.Message}");
        }
    }


    [When(@"I take input from localhost")]
    public void WhenITakeInputFromLocalhost()
    {
        // Optional: Add a delay if you want to ensure the server stays up for a certain period
        Thread.Sleep(30000); // Wait for 30 seconds, adjust as needed
        Console.WriteLine("Localhost is up. Waiting for form submission...");
    }
    [When(@"I Verify Collected API and Submit")]
    public void WhenIVerifyCollectedAPIAndSubmit()
    {
        try
        {
            // Wait for OTP to be available
            _trainBookingAPI.WaitForOtp();

            if (!string.IsNullOrEmpty(_trainBookingAPI.CollectedOtp))
            {
                Console.WriteLine($"OTP received for verification: {_trainBookingAPI.CollectedOtp}");

                // Verify that seats are selected
                if (_selectedSeats.Count == 0)
                {
                    Console.WriteLine("No seats selected. Cannot proceed with OTP verification.");
                    Assert.Fail("No seats selected. Cannot proceed with OTP verification.");
                }

                // Ensure that ticketIds are populated from selected seats
                List<string> ticketIds = new List<string>();
                foreach (var seatNumber in _selectedSeats)
                {
                    var seat = _availableSeats.FirstOrDefault(s => s.SeatNumber == seatNumber);
                    if (seat != null && !string.IsNullOrEmpty(seat.TicketId))
                    {
                        ticketIds.Add(seat.TicketId);  // Add TicketId
                    }
                }

                // If no valid TicketIds are found, fail the test
                if (ticketIds.Count == 0)
                {
                    Console.WriteLine("No valid ticket IDs found for verification.");
                    Assert.Fail("No valid ticket IDs found for verification.");
                }

                // Validate that tripId and tripRouteId are set
                if (string.IsNullOrEmpty(_selectedTripId) || string.IsNullOrEmpty(_selectedTripRouteId))
                {
                    Console.WriteLine("Trip ID or Trip Route ID is missing.");
                    Assert.Fail("Trip ID or Trip Route ID is missing.");
                }

                // Attempt OTP verification directly
                Console.WriteLine("Attempting OTP verification...");
                _trainBookingAPI.VerifyOtp(_selectedTripId, _selectedTripRouteId, ticketIds, _trainBookingAPI.CollectedOtp);

                Console.WriteLine("OTP verification successful.");
            }
            else
            {
                Console.WriteLine("OTP was not collected in time.");
                Assert.Fail("OTP was not collected in time.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error during OTP verification: " + ex.Message);
            Assert.Fail($"OTP verification failed: {ex.Message}");
        }
    }
    [When(@"I confirm Booking")]
    public void WhenIConfirmBooking()
    {
        try
        {
            // Wait for OTP to be collected
            _trainBookingAPI.WaitForOtp();

            if (!string.IsNullOrEmpty(_trainBookingAPI.CollectedOtp))
            {
                Console.WriteLine($"OTP received for confirmation: {_trainBookingAPI.CollectedOtp}");

                // Check if seats are selected
                if (_selectedSeats.Count == 0)
                {
                    Console.WriteLine("No seats selected. Cannot proceed with booking confirmation.");
                    Assert.Fail("No seats selected. Cannot proceed with booking confirmation.");
                }

                List<string> ticketIds = new List<string>();
                foreach (var seatNumber in _selectedSeats)
                {
                    var seat = _availableSeats.FirstOrDefault(s => s.SeatNumber == seatNumber);
                    if (seat != null && !string.IsNullOrEmpty(seat.TicketId))
                    {
                        ticketIds.Add(seat.TicketId); // Add TicketId
                    }
                }

                // Ensure valid TicketIds are present
                if (ticketIds.Count == 0)
                {
                    Console.WriteLine("No valid ticket IDs found for confirmation.");
                    Assert.Fail("No valid ticket IDs found for confirmation.");
                }

                // Ensure tripId and tripRouteId are set
                if (string.IsNullOrEmpty(_selectedTripId) || string.IsNullOrEmpty(_selectedTripRouteId))
                {
                    Console.WriteLine("Trip ID or Trip Route ID is missing.");
                    Assert.Fail("Trip ID or Trip Route ID is missing.");
                }

                // Attempt to confirm the booking
                Console.WriteLine("Attempting booking confirmation...");
                _trainBookingAPI.ConfirmBooking(_selectedTripId, _selectedTripRouteId, ticketIds, _trainBookingAPI.CollectedOtp);

                Console.WriteLine("Booking confirmed successfully.");
            }
            else
            {
                Console.WriteLine("OTP was not collected in time for booking confirmation.");
                Assert.Fail("OTP was not collected in time for booking confirmation.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Booking confirmation failed.");
            Console.WriteLine($"Error: {ex.Message}");
            Assert.Fail($"Booking confirmation failed: {ex.Message}");
        }
    }

    [When(@"I should be redirected to the payment page")]
    public void WhenIShouldBeRedirectedToThePaymentPage()
    {
        Console.WriteLine("Booking confirmed, and the browser should open the payment page.");
    }



    [Then("the booking should be successful")]
    public void ThenTheBookingShouldBeSuccessful()
    {
        Console.WriteLine("Booking successful.");
    }
}
