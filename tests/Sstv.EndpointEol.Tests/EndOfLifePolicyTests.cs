using System.Globalization;

namespace Sstv.EndpointEol.Tests;

/// <summary>
/// Tests for parsing and phase calculation of <see cref="EndOfLifePolicy"/>.
/// </summary>
[TestFixture]
public sealed class EndOfLifePolicyTests
{
    [Test]
    public void From_WhenDatesAreValid_ReturnsParsedPolicy()
    {
        var attribute = new EndOfLifeAttribute("2027-01-01")
        {
            WarnAfter = "2026-10-23",
            ReplacedBy = "/api/v2/test"
        };

        var policy = EndOfLifePolicy.From(attribute);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(policy.BlockAfter, Is.EqualTo(new DateOnly(2027, 1, 1)));
            Assert.That(policy.WarnAfter, Is.EqualTo(new DateOnly(2026, 10, 23)));
            Assert.That(policy.ReplacedBy, Is.EqualTo("/api/v2/test"));
        }
    }

    [TestCase("bad date")]
    [TestCase("2027.01.01")]
    [TestCase("2027-1-1")]
    public void From_WhenBlockAfterHasInvalidFormat_Throws(string blockAfter)
    {
        var attribute = new EndOfLifeAttribute(blockAfter);

        Assert.Throws<InvalidOperationException>(() => EndOfLifePolicy.From(attribute));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void AttributeCtor_WhenBlockAfterIsNullOrWhiteSpace_Throws(string blockAfter)
    {
        Assert.Throws<ArgumentException>(() => new EndOfLifeAttribute(blockAfter));
    }

    [Test]
    public void From_WhenWarnAfterIsNotEarlierThanBlockAfter_Throws()
    {
        var attribute = new EndOfLifeAttribute("2027-01-01")
        {
            WarnAfter = "2027-01-01"
        };

        var exception = Assert.Throws<InvalidOperationException>(() => EndOfLifePolicy.From(attribute));

        Assert.That(exception.Message, Does.Contain("must be earlier"));
    }

    [TestCase("2026-10-22", EndOfLifePhase.Active)]
    [TestCase("2026-10-23", EndOfLifePhase.Warning)]
    [TestCase("2026-12-31", EndOfLifePhase.Warning)]
    [TestCase("2027-01-01", EndOfLifePhase.Blocked)]
    [TestCase("2030-06-15", EndOfLifePhase.Blocked)]
    public void GetPhase_OnBoundaryDates_ReturnsExpectedPhase(string todayString, EndOfLifePhase expected)
    {
        var policy = EndOfLifePolicy.From(new EndOfLifeAttribute("2027-01-01") { WarnAfter = "2026-10-23" });
        var today = DateOnly.ParseExact(todayString, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        Assert.That(policy.GetPhase(today), Is.EqualTo(expected));
    }

    [Test]
    public void From_WhenWarnAfterNotSet_AppliesDefaultWarningWindow()
    {
        var policy = EndOfLifePolicy.From(new EndOfLifeAttribute("2027-01-01"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(policy.WarnAfter, Is.EqualTo(new DateOnly(2026, 10, 3)));
            Assert.That(policy.GetPhase(new DateOnly(2026, 10, 2)), Is.EqualTo(EndOfLifePhase.Active));
            Assert.That(policy.GetPhase(new DateOnly(2026, 10, 3)), Is.EqualTo(EndOfLifePhase.Warning));
            Assert.That(policy.GetPhase(new DateOnly(2027, 1, 1)), Is.EqualTo(EndOfLifePhase.Blocked));
        }
    }

    [Test]
    public void From_WhenBlockAfterIsInThePast_StillProducesAnEarlierWarnDate()
    {
        var policy = EndOfLifePolicy.From(new EndOfLifeAttribute("2020-01-01"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(policy.WarnAfter, Is.EqualTo(new DateOnly(2019, 10, 3)));
            Assert.That(policy.GetPhase(new DateOnly(2026, 1, 1)), Is.EqualTo(EndOfLifePhase.Blocked));
        }
    }
}
