using NUnit.Framework;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace HtmlToOpenXml.Tests
{
    /// <summary>
    /// Tests Horizontal Lines.
    /// </summary>
    [TestFixture]
    public class HrTests : HtmlConverterTestBase
    {
        [Test]
        public void Standalone_ReturnsWithNoSpacing ()
        {
            var elements = converter.Parse("<hr>");
            AssertIsHr(elements[0], false);
        }

        [Test(Description = "should not generate a particular spacing because border-bottom is empty")]
        public void AfterBorderlessContent_ReturnsWithNoSpacing ()
        {
            var elements = converter.Parse("<p style='border-top:1px solid black'>Before</p><hr>");
            AssertIsHr(elements[1], false);
        }

        [TestCase("<p style='border:0.1px solid black'>Before</p><hr>")]
        [TestCase("<p style='border-bottom:1px solid black'>Before</p><hr>")]
        [TestCase("<table><tr><td>Cell</td></tr></table><hr>")]
        public void AfterBorderedContent_ReturnsWithSpacing (string html)
        {
            var elements = converter.Parse(html);
            AssertIsHr(elements[1], true);
        }

        private static void AssertIsHr (OpenXmlCompositeElement hr, bool expectSpacing)
        {
            Assert.That(hr.ChildElements, Has.Count.EqualTo(2));
            var props = hr.GetFirstChild<ParagraphProperties>();
            Assert.That(props, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(props.ChildElements, Has.Count.EqualTo(expectSpacing ? 2 : 1));
                Assert.That(props.ParagraphBorders, Is.Not.Null);
                Assert.That(props.ParagraphBorders?.TopBorder, Is.Not.Null);
            });

            Assert.That(props.SpacingBetweenLines, expectSpacing? Is.Not.Null : Is.Null);
        }
    }
}