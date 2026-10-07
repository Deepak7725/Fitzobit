using Fitzobit.Application.Imports.Locations;
using Fitzobit.Application.Imports.Validation;

namespace Fitzobit.Tests.Imports;

public class FieldValidationTests
{
    private static readonly LocationClassificationService Locations = new(ImportHarness.LocationOptions);

    [Theory]
    [InlineData("9876543210", PhoneCheck.ValidMobile, "9876543210")]
    [InlineData("+91 98765 43210", PhoneCheck.ValidMobile, "9876543210")]
    [InlineData("0091-98765-43210", PhoneCheck.ValidMobile, "9876543210")]
    [InlineData("09876543210", PhoneCheck.ValidMobile, "9876543210")]
    [InlineData("(0) 98765 43210", PhoneCheck.ValidMobile, "9876543210")]
    [InlineData("4362234567", PhoneCheck.Landline, "4362234567")]
    [InlineData("9.87654E+09", PhoneCheck.ScientificNotation, null)]
    [InlineData("98765432", PhoneCheck.Invalid, "98765432")]
    [InlineData("1234567890", PhoneCheck.Invalid, "1234567890")]
    [InlineData("98765abc10", PhoneCheck.ContainsLetters, null)]
    [InlineData("98765 43210 / 91234 56789", PhoneCheck.Invalid, null)]
    [InlineData("  ", PhoneCheck.Missing, null)]
    public void Indian_phone_numbers_are_validated(string input, PhoneCheck expected, string? normalized)
    {
        var result = PhoneNumberValidator.Validate(input);
        Assert.Equal(expected, result.Check);
        if (expected != PhoneCheck.Invalid || normalized != null) Assert.Equal(normalized, result.Normalized);
    }

    [Fact]
    public void Pincode_is_extracted_without_changing_the_original()
    {
        const string original = "RS Puram, Coimbatore - 641002";
        var c = Locations.Classify(original);

        Assert.Equal(original, c.OriginalLocation);
        Assert.Equal("641002", c.Pincode);
        Assert.Equal(PincodeStatus.Detected, c.PincodeStatus);
        Assert.Equal("rs puram, coimbatore", c.NormalizedLocation);
        Assert.Equal("RS Puram", c.Locality);
        Assert.Equal("Coimbatore", c.City);
    }

    [Theory]
    [InlineData("12, 3rd Cross Street, Medical College Road, Thanjavur, Tamil Nadu 613 004", "613004", "Medical College Road", "Thanjavur")]
    [InlineData("Chennai - Anna Nagar", null, "Anna Nagar", "Chennai")]
    [InlineData("Door 4, Gandhi Road, Tanjore PIN: 613001", "613001", "Gandhi Road", "Thanjavur")]
    [InlineData("Near bus stand, Kumbakonam", null, "Near bus stand", "Kumbakonam")]
    [InlineData("45 Medical College Road, Thanjavur - 613004", "613004", "Medical College Road", "Thanjavur")]
    [InlineData("Door 4, Vallam Main Road, Vallam, Thanjavur PIN 613403", "613403", "Vallam", "Thanjavur")]
    [InlineData("No. 12/3A, Gandhi Road, Thanjavur", null, "Gandhi Road", "Thanjavur")]
    public void Addresses_are_classified(string address, string? pincode, string? locality, string? city)
    {
        var c = Locations.Classify(address);
        Assert.Equal(pincode, c.Pincode);
        Assert.Equal(locality, c.Locality);
        Assert.Equal(city, c.City);
    }

    [Fact]
    public void Door_numbers_keep_addresses_distinct_but_share_the_area()
    {
        var a = Locations.Classify("12 Medical College Road, Thanjavur - 613004");
        var b = Locations.Classify("45 Medical College Road, Thanjavur - 613004");
        Assert.NotEqual(a.NormalizedLocation, b.NormalizedLocation);
        Assert.Equal(a.Locality, b.Locality);
    }

    [Fact]
    public void Phone_numbers_inside_addresses_are_not_taken_as_pincodes()
    {
        var c = Locations.Classify("Flat 2, Lake View, Madurai (call 9876543210)");
        Assert.Null(c.Pincode);
        Assert.Equal(PincodeStatus.NotDetected, c.PincodeStatus);
    }

    [Fact]
    public void Several_pincodes_are_not_guessed()
    {
        var c = Locations.Classify("Office 600040 / Home 641002, Chennai");
        Assert.Null(c.Pincode);
        Assert.Equal(PincodeStatus.Multiple, c.PincodeStatus);
        Assert.Equal(["600040", "641002"], c.PincodeCandidates);
    }

    [Fact]
    public void A_labelled_pincode_wins_over_other_six_digit_numbers()
    {
        var c = Locations.Classify("Plot 110011, Gandhi Nagar, Thanjavur, Pincode 613001");
        Assert.Equal("613001", c.Pincode);
    }
}
