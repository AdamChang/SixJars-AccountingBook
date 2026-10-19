using FluentAssertions;
using SixJars.Domain.Books;
using SixJars.Domain.Common;
using Xunit;

namespace SixJars.Domain.Tests.Books;

public class BookTagTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 19, 1, 0, 0, TimeSpan.Zero);
    private readonly Book _book = new("測試帳本", new DateOnly(2025, 12, 30));

    [Fact]
    public void Add_tag_trims_name_and_appends_sort_order()
    {
        var trip = _book.AddTag("  日本旅遊 ", TagColor.Teal);
        var work = _book.AddTag("工作", TagColor.Blue);

        (trip.Name, trip.Color, trip.IsArchived).Should().Be(("日本旅遊", TagColor.Teal, false));
        (trip.SortOrder, work.SortOrder).Should().Be((0, 1));
        _book.Tags.Should().Equal(trip, work);
    }

    [Fact]
    public void Tag_names_are_unique_ignoring_case_and_whitespace()
    {
        _book.AddTag("Trip", TagColor.Red);

        var act = () => _book.AddTag(" trip ", TagColor.Blue);
        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.RuleCode);
    }

    [Fact]
    public void Archived_tags_still_count_for_uniqueness()
    {
        var trip = _book.AddTag("旅遊", TagColor.Red);
        _book.ArchiveTag(trip.Id, At);

        var act = () => _book.AddTag("旅遊", TagColor.Blue);
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("一二三四五六七八")]
    [InlineData("✈️日本旅遊")]
    [InlineData("✈️✈️✈️✈️✈️✈️✈️✈️")]
    [InlineData("👨‍👩‍👧‍👦家庭")]
    public void Tag_name_up_to_8_text_elements_is_allowed(string name)
    {
        _book.AddTag(name, TagColor.Red).Name.Should().Be(name);
    }

    [Theory]
    [InlineData("一二三四五六七八九")]
    [InlineData("abcdefghi")]
    public void Tag_name_longer_than_8_text_elements_is_rejected(string name)
    {
        var act = () => _book.AddTag(name, TagColor.Red);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Tag_name_cannot_be_blank()
    {
        var act = () => _book.AddTag("  ", TagColor.Red);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Undefined_color_is_rejected()
    {
        var act = () => _book.AddTag("旅遊", (TagColor)99);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Update_tag_renames_and_recolors_keeping_names_unique()
    {
        var trip = _book.AddTag("Trip", TagColor.Red);
        _book.AddTag("Work", TagColor.Blue);

        _book.UpdateTag(trip.Id, " TRIP ", TagColor.Green);   // 改成自己原本的名字（只差大小寫）不算重複
        (trip.Name, trip.Color).Should().Be(("TRIP", TagColor.Green));

        var act = () => _book.UpdateTag(trip.Id, "work", TagColor.Green);
        act.Should().Throw<DomainException>();
        var tooLong = () => _book.UpdateTag(trip.Id, "一二三四五六七八九", TagColor.Green);
        tooLong.Should().Throw<DomainException>();
    }

    [Fact]
    public void Archive_keeps_first_time_and_unarchive_clears_it()
    {
        var trip = _book.AddTag("旅遊", TagColor.Red);

        _book.ArchiveTag(trip.Id, At);
        _book.ArchiveTag(trip.Id, At.AddDays(1));
        trip.ArchivedAt.Should().Be(At);

        _book.UnarchiveTag(trip.Id);
        trip.IsArchived.Should().BeFalse();
    }

    [Fact]
    public void Reorder_tags_requires_exact_members()
    {
        var a = _book.AddTag("A", TagColor.Red);
        var b = _book.AddTag("B", TagColor.Red);
        var c = _book.AddTag("C", TagColor.Red);

        _book.ReorderTags([c.Id, a.Id, b.Id]);
        (a.SortOrder, b.SortOrder, c.SortOrder).Should().Be((1, 2, 0));

        var missing = () => _book.ReorderTags([c.Id, a.Id]);
        missing.Should().Throw<DomainException>();
        var duplicated = () => _book.ReorderTags([c.Id, a.Id, a.Id]);
        duplicated.Should().Throw<DomainException>();
    }

    [Fact]
    public void Referenced_tag_cannot_be_removed()
    {
        var trip = _book.AddTag("旅遊", TagColor.Red);

        var act = () => _book.RemoveTag(trip.Id, isReferenced: true);
        act.Should().Throw<DomainException>().Which.Code.Should().Be(DomainException.InUseCode);
        _book.Tags.Should().ContainSingle();
    }

    [Fact]
    public void Remove_tag_compacts_sort_order()
    {
        var a = _book.AddTag("A", TagColor.Red);
        var b = _book.AddTag("B", TagColor.Red);
        var c = _book.AddTag("C", TagColor.Red);

        _book.RemoveTag(a.Id, isReferenced: false);

        _book.Tags.Should().Equal(b, c);
        (b.SortOrder, c.SortOrder).Should().Be((0, 1));
    }

    [Fact]
    public void Get_unknown_tag_throws()
    {
        var act = () => _book.GetTag(TagId.New());
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Add_tag_keeps_given_id()
    {
        var id = TagId.New();
        _book.AddTag("旅遊", TagColor.Red, id).Id.Should().Be(id);
        _book.GetTag(id).Name.Should().Be("旅遊");
    }
}
