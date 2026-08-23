using System;
using Egghead.SaveSystem;
using NUnit.Framework;

public class SaveDataValidatorTests
{
    private static readonly DateTime Timestamp = new(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public void ValidCurrentBoardIsAccepted()
    {
        Assert.That(Validate(ValidSave()).IsValid, Is.True);
    }

    [Test]
    public void ValidLegacyBoardIsMigrated()
    {
        SaveData data = ValidSave();
        data.SchemaVersion = 0;

        SaveValidationResult result = Validate(data);

        Assert.That(result.IsValid, Is.True);
        Assert.That(result.WasMigrated, Is.True);
        Assert.That(result.Data.SchemaVersion, Is.EqualTo(1));
    }

    [Test]
    public void EmptyZeroScoreSaveIsValidButNotResumable()
    {
        SaveData data = ValidSave();
        data.Score = 0;
        data.LetterTileData = null;

        Assert.That(Validate(data).IsValid, Is.True);
    }

    [Test]
    public void NullBoardWithScoreIsRejected()
    {
        SaveData data = ValidSave();
        data.LetterTileData = null;
        Assert.That(Validate(data).IsValid, Is.False);
    }

    [TestCase(-1)]
    [TestCase(2)]
    [TestCase(99)]
    public void UnsupportedSchemaIsRejected(int version)
    {
        SaveData data = ValidSave();
        data.SchemaVersion = version;
        Assert.That(Validate(data).IsValid, Is.False);
    }

    [Test]
    public void NegativeScoreIsRejected()
    {
        SaveData data = ValidSave();
        data.Score = -1;
        Assert.That(Validate(data).IsValid, Is.False);
    }

    [Test]
    public void MissingTimestampIsRejected()
    {
        SaveData data = ValidSave();
        data.Timestamp = default;
        Assert.That(Validate(data).IsValid, Is.False);
    }

    [Test]
    public void UnspecifiedTimestampIsNormalizedAsUtc()
    {
        SaveData data = ValidSave();
        data.Timestamp = DateTime.SpecifyKind(Timestamp, DateTimeKind.Unspecified);
        SaveValidationResult result = Validate(data);
        Assert.That(result.Data.Timestamp.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(result.Data.Timestamp, Is.EqualTo(Timestamp));
    }

    [Test]
    public void WrongColumnCountIsRejected()
    {
        SaveData data = ValidSave();
        data.LetterTileData = new SavedLetterTileData[6][];
        Assert.That(Validate(data).IsValid, Is.False);
    }

    [Test]
    public void NullColumnIsRejected()
    {
        SaveData data = ValidSave();
        data.LetterTileData[2] = null;
        Assert.That(Validate(data).IsValid, Is.False);
    }

    [Test]
    public void WrongRowCountIsRejected()
    {
        SaveData data = ValidSave();
        data.LetterTileData[1] = new SavedLetterTileData[7];
        Assert.That(Validate(data).IsValid, Is.False);
    }

    [Test]
    public void WrongCoordinatesAreRejected()
    {
        SaveData data = ValidSave();
        data.LetterTileData[0][0].column = 1;
        Assert.That(Validate(data).IsValid, Is.False);
    }

    [TestCase('@')]
    [TestCase('a')]
    [TestCase('[')]
    public void UnsupportedLetterIsRejected(char letter)
    {
        SaveData data = ValidSave();
        data.LetterTileData[0][0].letter = letter;
        Assert.That(Validate(data).IsValid, Is.False);
    }

    [TestCase(-1)]
    [TestCase(5)]
    public void InvalidTileTypeIsRejected(int tileType)
    {
        SaveData data = ValidSave();
        data.LetterTileData[0][0].tileType = tileType;
        Assert.That(Validate(data).IsValid, Is.False);
    }

    private static SaveValidationResult Validate(SaveData data) => SaveDataValidator.ValidateAndNormalize(data);

    private static SaveData ValidSave()
    {
        return new SaveData
        {
            SchemaVersion = 1,
            Score = 10,
            Timestamp = Timestamp,
            LetterTileData = SaveReconcilerTests.Board()
        };
    }
}
