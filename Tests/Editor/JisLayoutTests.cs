using NUnit.Framework;
using hacha.LiveCodingEditor;

namespace hacha.LiveCodingEditor.Tests
{
    public class JisLayoutTests
    {
        [TestCase('@', '"')]
        [TestCase('^', '&')]
        [TestCase('&', '\'')]
        [TestCase('*', '(')]
        [TestCase('(', ')')]
        [TestCase('_', '=')]
        [TestCase('=', '^')]
        [TestCase('+', '~')]
        [TestCase('[', '@')]
        [TestCase('{', '`')]
        [TestCase(']', '[')]
        [TestCase('}', '{')]
        [TestCase('\\', ']')]
        [TestCase('|', '}')]
        [TestCase(':', '+')]
        [TestCase('\'', ':')]
        [TestCase('"', '*')]
        public void MapsUsSymbolToJisSymbolOnSameKey(char us, char jis)
        {
            Assert.AreEqual(jis, JisLayout.FromUs(us));
        }

        [TestCase('a')]
        [TestCase('Z')]
        [TestCase('0')]
        [TestCase('-')]
        [TestCase(';')]
        [TestCase(',')]
        [TestCase('.')]
        [TestCase('/')]
        [TestCase('!')]
        [TestCase('#')]
        [TestCase('$')]
        [TestCase('%')]
        [TestCase('?')]
        [TestCase(' ')]
        public void PassesThroughKeysThatMatch(char c)
        {
            Assert.AreEqual(c, JisLayout.FromUs(c));
        }

        [Test]
        public void ShiftZeroProducesNothing()
        {
            Assert.AreEqual('\0', JisLayout.FromUs(')'));
        }
    }
}
