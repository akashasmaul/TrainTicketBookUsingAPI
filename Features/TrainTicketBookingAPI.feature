Feature: Train Ticket Booking via API

  Scenario: Search and book 4 seats for a trip
    Given I authenticate with the API
    When I search for trips from 'From' to 'To' on 'Date' with seat class 'SNIGDHA' and trip number 'TrainNumber'
    And I get available seats for the trip
 #   And I book the selected seats
 #   And I continue to purchase for verification
 #   And I take input from localhost
 #  And I Verify Collected API and Submit
 #   And I confirm Booking
 #   And I should be redirected to the payment page
 #   Then the booking should be successful
    