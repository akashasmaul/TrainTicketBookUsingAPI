# TrainTicketBookUsingAPI

## Overview

**TrainTicketBookUsingAPI** is an automated test suite for booking train tickets via an API. The project uses **SpecFlow** with **NUnit** to simulate searching for train trips, selecting seats, and confirming bookings through API interactions.

## Features

- **User Authentication**: Logs in using valid credentials.  
- **Trip Search**: Searches available trips based on date, class, and route.  
- **Seat Selection**: Fetches and selects available seats.  
- **Ticket Booking**: Reserves selected seats.  
- **Purchase Verification**: Validates ticket purchase with OTP.  
- **Booking Confirmation**: Confirms booking and redirects to payment.  

## Test Scenario

```gherkin
Feature: Train Ticket Booking via API

  Scenario: Search and book 4 seats for a trip
    Given I authenticate with the API
    When I search for trips from 'From' to 'To' on 'Date' with seat class 'SNIGDHA' and trip number 'TrainNumber'
    And I get available seats for the trip
    And I book the selected seats
    And I continue to purchase for verification
    And I take input from localhost
    And I Verify Collected API and Submit
    And I confirm Booking
    And I should be redirected to the payment page
    Then the booking should be successful

## Technologies Used
- C# (NUnit, SpecFlow)
- SpecFlow (for BDD test scenarios)
- Train API (to interact with the train booking system)

## Usage
 - Update Credentials.cs with valid API credentials.
 - Modify Feature Files in Features/TrainTicketBooking.feature as needed.
 - Execute tests using the NUnit test runner.

## Contact
- For any queries, contact: xakshhh@gmail.com
