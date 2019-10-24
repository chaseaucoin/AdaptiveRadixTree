﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;


namespace System.Collections.Specialized
{
    /// <summary>
    ///     Simplified wildcard search comparer.
    ///     Works like a regex, but with a wildcard syntax (ie: ?* characters).
    /// </summary>
    public sealed class WildcardRegex {
        private const char DEFAULT_WILDCARD_UNKNOWN  = '?';
        private const char DEFAULT_WILDCARD_ANYTHING = '*';

        public readonly string WildcardFormat;
        public readonly SearchOption Option;
        
        /// <summary>?</summary>
        private readonly char m_wildcardUnknown;
        /// <summary>*</summary>
        private readonly char m_wildcardAnything;

            
        private readonly bool m_resultMustMatchAtStart; // result/match must start at 0.
        private readonly bool m_resultMustMatchAtEnd;   // result/match must end at {start+length}.
        private readonly int m_totalCharacters;
        private readonly ConsecutiveParseSection[] m_sections; // if Length==0, means format = '*'

        #region constructors
        /// <param name="wildcard_format">The wildcard pattern. ex: '20??-01-01*'</param>
        public WildcardRegex(string wildcard_format, SearchOption option = SearchOption.ExactMatch, char wildcard_unknown_character = DEFAULT_WILDCARD_UNKNOWN, char wildcard_anything_character = DEFAULT_WILDCARD_ANYTHING) {
            if(string.IsNullOrEmpty(wildcard_format))
                throw new FormatException(nameof(wildcard_format));

            this.WildcardFormat = wildcard_format;
            this.Option         = option;
            m_wildcardUnknown   = wildcard_unknown_character;
            m_wildcardAnything  = wildcard_anything_character;

            m_sections        = this.ParseSearchFormat(wildcard_format);
            m_totalCharacters = m_sections.Sum(section => section.Length + section.WildcardUnknownBefore + section.WildcardUnknownAfter);

            if(option == SearchOption.ExactMatch) {
                // not sure if we should check if theres a non-? character before first * 
                m_resultMustMatchAtStart = wildcard_format[0] != m_wildcardAnything;
                m_resultMustMatchAtEnd   = wildcard_format[wildcard_format.Length - 1] != m_wildcardAnything;
            }
        }
        #endregion

        #region IsMatch()
        public bool IsMatch(string value) {
            return this.Match(value, 0, value.Length).length >= 0;
        }
        public bool IsMatch(string value, int startIndex) {
            return this.Match(value, startIndex, value.Length - startIndex).length >= 0;
        }
        public bool IsMatch(string value, int startIndex, int length) {
            return this.Match(value, startIndex, length).length >= 0;
        }
        #endregion
        #region Match()
        /// <summary>
        ///     Returns length = 0 if search = '*'.
        ///     Returns length = -1 if not found.
        /// </summary>
        public (int start, int length) Match(string value) {
            return this.Match(value, 0, value.Length);
        }
        /// <summary>
        ///     Returns length = 0 if search = '*'.
        ///     Returns length = -1 if not found.
        /// </summary>
        public (int start, int length) Match(string value, int startIndex) {
            return this.Match(value, startIndex, value.Length - startIndex);
        }
        /// <summary>
        ///     Returns length = 0 if search = '*'.
        ///     Returns length = -1 if not found.
        /// </summary>
        public (int start, int length) Match(string value, int startIndex, int length) {
            // algorithm explanation
            // format = '123*456*?678'
            // sections = {123, 456, 678}
            // first makes sure the string starts with '123' (if beginswith)
            // then makes sure the string ends with '?678' (if endswith)
            // then make sure every other section from section 1+ are found in order and ends before the last one
            // if any section is not found, then there is no match

            if(length < m_totalCharacters)
                return (startIndex, -1);
            // special case if format = '*'
            if(m_sections.Length == 0)
                return (startIndex, 0);

            int index          = startIndex;
            int originalLength = length;
            int sectionIndex   = 0;
            int firstIndex     = -1; // first match
            int lastIndex      = -1; // last match end pos

            if(m_resultMustMatchAtStart) {
                var section = m_sections[0];
                index      += section.WildcardUnknownBefore;
                if(!this.StringEqualWithUnknownCharacters(value, index, this.WildcardFormat, section.Start, section.Length))
                    return (startIndex, -1);
                index  += section.WildcardUnknownAfter;
                length -= section.WildcardUnknownBefore + section.WildcardUnknownAfter;

                if(m_resultMustMatchAtEnd && m_sections.Length == 1)
                    return length == 0 ? (startIndex, originalLength) : (startIndex, -1);
                
                firstIndex   = 0;
                sectionIndex = 1;
            }

            if(m_resultMustMatchAtEnd) {
                var section = m_sections[m_sections.Length - 1];
                int pos     = startIndex + originalLength - section.WildcardUnknownAfter - section.Length;
                if(pos - section.WildcardUnknownBefore < index || !this.StringEqualWithUnknownCharacters(value, pos, this.WildcardFormat, section.Start, section.Length))
                    return (startIndex, -1);
                lastIndex = startIndex + originalLength;
                length   -= section.WildcardUnknownBefore + section.Length + section.WildcardUnknownAfter;
            }

            int last        = -1;
            int lastSection = m_sections.Length - (m_resultMustMatchAtEnd ? 1 : 0);
            
            while(sectionIndex < lastSection && length > 0) {
                var section = m_sections[sectionIndex];
                if(section.Length > 0) {
                    last = this.StringIndexOfWithUnknownCharacters(value, index, length, in section);
                    if(last < 0)
                        return (startIndex, -1);
                    if(sectionIndex <= 1 && firstIndex < 0)
                        firstIndex = last;
                    var new_index = last + section.WildcardUnknownBefore + section.Length + section.WildcardUnknownAfter;
                    length       -= new_index - index;
                    index         = new_index;
                } else {
                    // case where format='??'  or  'aa*?'
                    last    = index;
                    index  += section.WildcardUnknownBefore + section.WildcardUnknownAfter;
                    length -= section.WildcardUnknownBefore + section.WildcardUnknownAfter;
                }
                sectionIndex++;
            }

            if(sectionIndex != lastSection || length < 0)
                return (startIndex, -1);

            if(!m_resultMustMatchAtEnd) {
                var section = m_sections[m_sections.Length - 1];
                lastIndex = last + section.WildcardUnknownBefore + section.Length + section.WildcardUnknownAfter;
            }

            return (firstIndex, lastIndex - firstIndex);
        }
        #endregion
        #region Matches()
        /// <summary>
        ///     Returns length = 0 if search = '*'.
        ///     Returns length = -1 if not found.
        /// </summary>
        public IEnumerable<(int start, int length)> Matches(string value) {
            return this.Matches(value, 0, value.Length);
        }
        /// <summary>
        ///     Returns length = 0 if search = '*'.
        ///     Returns length = -1 if not found.
        /// </summary>
        public IEnumerable<(int start, int length)> Matches(string value, int startIndex) {
            return this.Matches(value, startIndex, value.Length - startIndex);
        }
        /// <summary>
        ///     Returns length = 0 if search = '*'.
        ///     Returns length = -1 if not found.
        /// </summary>
        public IEnumerable<(int start, int length)> Matches(string value, int startIndex, int length) {
            // special case, if format = '*', then return only one match instead of infinity match
            if(m_sections.Length == 0) {
                yield return (startIndex, 0);
                yield break;
            }

            int end = startIndex + length;
            while(true) {
                var res = this.Match(value, startIndex, length);

                if(res.length >= 0) {
                    yield return res;
                    if(m_resultMustMatchAtStart || m_resultMustMatchAtEnd)
                        yield break;
                    // dont allow overlapping results
                    startIndex = res.start + res.length;
                    length     = end - startIndex;
                } else
                    yield break;
            }
        }
        #endregion
        #region static ToRegex()
        public string ToRegex(RegexFormat regex_format = RegexFormat.DotNet) {
            int capacity = this.WildcardFormat.Length;
            if(capacity <= 4096)
                capacity *= 2;
            else {
                // fast approximate count - not meant to be an exact count
                capacity += 2;
                for(int i = 0; i < this.WildcardFormat.Length; i++) {
                    var c = this.WildcardFormat[i];
                    if(c == m_wildcardAnything)
                        capacity += 2;
                    else if(!IsAlphaNumeric(c))
                        capacity++;
                }
            }

            var sb = new StringBuilder(capacity);

            if(regex_format == RegexFormat.SQL)
                sb.Append('\'');
            if(m_resultMustMatchAtStart)
                sb.Append('^');

            // special case: means the format = '*'
            if(m_sections.Length == 0)
                sb.Append(".*");

            for(int j = 0; j < m_sections.Length; j++) {
                var section = m_sections[j];

                if(j > 0)
                    sb.Append(".*");

                for(int i = 0; i < section.WildcardUnknownBefore; i++)
                    sb.Append('.');

                for(int i = 0; i < section.Length; i++) {
                    var c = this.WildcardFormat[section.Start + i];

                    if(c == m_wildcardUnknown)
                        sb.Append('.');
                    else if(regex_format == RegexFormat.SQL && c == '\'')
                        sb.Append("''");
                    else {
                        if(!IsAlphaNumeric(c))
                            sb.Append('\\');
                        sb.Append(c);
                    }
                }

                for(int i = 0; i < section.WildcardUnknownAfter; i++)
                    sb.Append('.');
            }

            if(m_resultMustMatchAtEnd)
                sb.Append('$');
            if(regex_format == RegexFormat.SQL)
                sb.Append('\'');

            return sb.ToString();

            bool IsAlphaNumeric(char c) {
                //char.IsLetterOrDigit(c)
                return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
            }
        }
        /// <param name="wildcard_format">The wildcard pattern. ex: '20??-01-01*'</param>
        public static string ToRegex(string wildcard_format, SearchOption option = SearchOption.ExactMatch, char wildcard_unknown_character = DEFAULT_WILDCARD_UNKNOWN, char wildcard_anything_character = DEFAULT_WILDCARD_ANYTHING, RegexFormat regex_format = RegexFormat.DotNet) {
            return new WildcardRegex(wildcard_format, option, wildcard_unknown_character, wildcard_anything_character)
                .ToRegex(regex_format);
        }
        #endregion

        #region private ParseSearchFormat()
        private ConsecutiveParseSection[] ParseSearchFormat(string format) {
            var sections = this.ParseSearchFormatSections(format)
                .Where(o => o.len > 0) // avoids empty sections in cases such as 'aa**aa', '*aa' and 'aa*'
                .Select(o => new ParsingSection(){ Start = o.start, Length = o.len })
                .ToList();

            for(int i = 0; i < m_sections.Length; i++) {
                var section = sections[i];
                
                // TrimStart(this.WildcardUnknown)
                while(section.Length > 0 && format[section.Start] == m_wildcardUnknown) {
                    section.Length--;
                    section.Start++;
                    section.WildcardUnknownBefore++;
                }
                // TrimEnd(this.WildcardUnknown)
                while(section.Length > 0 && format[section.Start + section.Length - 1] == m_wildcardUnknown) {
                    section.Length--;
                    section.WildcardUnknownAfter++;
                }
            }

            // merge '??' section with prev
            // ex: 'abc*??*456' -> 'abc??*456'
            int index = 1;
            while(index < sections.Count - (m_resultMustMatchAtEnd ? 1 : 0)) {
                var section = sections[index];
                if(section.Length == 0 && (section.WildcardUnknownBefore > 0 || section.WildcardUnknownAfter > 0)) {
                    sections[index - 1].WildcardUnknownAfter += section.WildcardUnknownBefore + section.WildcardUnknownAfter;
                    sections.RemoveAt(index);
                } else 
                    index++;
            }
            // move ['??' at section start] to [previous section end] for faster parse
            // ex: 'abc?*??456' -> 'abc???*456'
            index = 1;
            while(index < sections.Count) {
                var section = sections[index];
                if(section.WildcardUnknownBefore > 0) {
                    sections[index - 1].WildcardUnknownAfter += section.WildcardUnknownBefore;
                    section.WildcardUnknownBefore = 0;
                }
                index++;
            }

            var res = new ConsecutiveParseSection[sections.Count];
            for(int i = 0; i < res.Length; i++) {
                var section = sections[i];

                // find longest stretch of non-WildcardUnknown characters
                // could also look at the stretch with the most repeated characters, which should speed up the search
                var best_sub_section = SplitPosition(format, section.Start, section.Length, m_wildcardUnknown)
                    .Select(o => {
                        int duplicates   = 0;
                        int consecutives = 0;
                        // very common case: the entire section does not contain any ?, which means we have only one section
                        if(o.length != section.Length){
                            var visitedChars = new HashSet<char>(o.length);
                            char prev = '\0';
                            for(int j = 0; j < o.length; j++) {
                                var c = format[o.start + j];
                                if(!visitedChars.Add(c))
                                    duplicates++;
                                if(j > 0 && c == prev)
                                    consecutives++;
                                prev = c;
                            }
                            duplicates -= consecutives;
                        }
                        return new { o.start, o.length, duplicates, consecutives };
                    })
                    // trying to figure out if searching '00' over '12345' is better
                    .OrderByDescending(o => (o.consecutives * 2 + 1) * (o.duplicates * 1.5 + 1)) // * o.length
                    .ThenByDescending(o => o.consecutives)
                    .ThenByDescending(o => o.duplicates)
                    .ThenByDescending(o => o.length)
                    .First();

                res[i] = new ConsecutiveParseSection(
                    section.Start, 
                    section.Length, 
                    section.WildcardUnknownBefore, 
                    section.WildcardUnknownAfter, 
                    format.Substring(best_sub_section.start, best_sub_section.length), //format.Substring(section.Start, section.Length),
                    best_sub_section.start);
            }
            return res;
        }
        private sealed class ParsingSection {
            public int Start;
            public int Length;
            public int WildcardUnknownBefore;
            public int WildcardUnknownAfter;
        }
        #endregion
        #region private ParseSearchFormatSections()
        /// <summary>
        ///     basically does format.Split(WildcardAnything)
        /// </summary>
        private IEnumerable<(int start, int len)> ParseSearchFormatSections(string format) {
            int start = 0;
            int len   = 0;

            for(int i = 0; i < format.Length; i++) {
                var c = format[i];
                len++;

                if(c == m_wildcardAnything) {
                    if(len > 1)
                        yield return (start, len - 1);

                    start = i + 1;
                    len   = 0;
                }
            }
            if(len > 0)
                yield return (start, len);
        }
        #endregion

        #region private StringEqualWithUnknownCharacters()
        /// <summary>
        ///     Returns true if the strings are equal, assuming search may contain WildcardUnknown '?'.
        ///     Search may not contain any WildcardAnything '*'.
        /// </summary>
        private bool StringEqualWithUnknownCharacters(string source, int sourceIndex, string search, int searchIndex, int count) {
            // ideally this would be a string.Equals() for faster speed, but there is no overload to specify start/length
            //string.CompareOrdinal(value, index, section.Search, 0, section.Length);

            for(int i = 0; i < count; i++) {
                var d = search[searchIndex + i];

                if(d != this.m_wildcardUnknown && source[sourceIndex + i] != d)
                    return false;
            }
            return true;
        }
        #endregion
        #region private StringIndexOfWithUnknownCharacters()
        /// <summary>
        ///     Returns the index of section, assuming the section may contain WildcardUnknown '?'.
        ///     Search may not contain any WildcardAnything '*'.
        /// </summary>
        private int StringIndexOfWithUnknownCharacters(string source, int index, int length, in ConsecutiveParseSection section) {
            int charsBeforeSearch = section.WildcardUnknownBefore + section.SearchIndex;
            int charsAfterSearch  = section.Length - section.Search.Length - section.SearchIndex;
            
            index          += charsBeforeSearch;
            length         -= charsBeforeSearch + charsAfterSearch + section.WildcardUnknownAfter;
            var compareInfo = System.Globalization.CultureInfo.InvariantCulture.CompareInfo;

            while(true) {
                //value.IndexOf(section.Search, startIndex, length, StringComparison.Ordinal);
                int pos = compareInfo.IndexOf(
                    source,
                    section.Search,
                    index,
                    length,
                    System.Globalization.CompareOptions.Ordinal);
                if(pos < 0)
                    return -1;

                bool startMatches = !section.ContainsCharsBeforeSearchIndex || this.StringEqualWithUnknownCharacters(source, pos - section.SearchIndex, this.WildcardFormat, section.Start, section.SearchIndex);
                if(!startMatches) {
                    var diff = (pos + 1) - index;
                    index  += diff;
                    length -= diff;
                    continue;
                }
                bool endMatches = !section.ContainsCharsAfterSearchIndex || this.StringEqualWithUnknownCharacters(source, pos + section.Search.Length, this.WildcardFormat, section.Start + section.SearchIndex + section.Search.Length, charsAfterSearch);
                if(!endMatches) {
                    var diff = (pos + 1) - index;
                    index  += diff;
                    length -= diff;
                    continue;
                }

                return pos - section.SearchIndex - section.WildcardUnknownBefore;
            }
        }
        #endregion
        #region private SplitPosition()
        /// <summary>
        ///     Same as string.Split(), but for returns positions instead.
        ///     ex: "abcde".SplitPosition(1, 4, new []{'b'}) = {(2,3)}
        /// </summary>
        private IEnumerable<(int start, int length)> SplitPosition(string source, int startIndex, int length, char separator) {
            int index = 0;
            int start = startIndex;
            int max   = startIndex + length;
            for(int i = startIndex; i < max; i++) {
                var c = source[i];
                if(c == separator) {
                    yield return (start, index - start);
                    start = index + 1;
                }
                index++;
            }
            yield return (start, index - start);
        }
        #endregion

        // potential todo for long strings: boyer-moore string search algorithm with pre-processed searches
        // problem is this wouldnt work well on short searches

        public enum SearchOption {
            /// <summary>
            ///     equivalent to "value = 'searchstring'"
            /// </summary>
            ExactMatch,
            /// <summary>
            ///     equivalent to "value LIKE '%searchstring%'"
            /// </summary>
            Partial,
        }
        public enum RegexFormat {
            DotNet,
            /// <summary>
            ///     The string to be passed in sql, ie: "where searchcolumn ~ value"
            /// </summary>
            SQL,
        }

        /// <summary>
        ///     Represents a section of consecutive characters without any WILDCARD_ANYTHING in it.
        ///     This may include multiple WILDCARD_UNKNOWN.
        /// </summary>
        private readonly struct ConsecutiveParseSection {
            public readonly int Start;
            public readonly int Length;
            public readonly int WildcardUnknownBefore; // how many WILDCARD_UNKNOWN are at the start of the section.
            public readonly int WildcardUnknownAfter;  // how many WILDCARD_UNKNOWN are at the end of the section.
            public readonly string Search;             // the optimal stretch of characters without WILDCARD_UNKNOWN to search for (takes into account length and # consecutive/repeated chars)
            public readonly int SearchIndex;           // the index starting from this.Start
            public readonly bool ContainsCharsBeforeSearchIndex;
            public readonly bool ContainsCharsAfterSearchIndex;

            public ConsecutiveParseSection(int start, int length, int wildcardBefore, int wildcardAfter, string search, int searchIndex) {
                this.Start                          = start;
                this.Length                         = length;
                this.WildcardUnknownBefore          = wildcardBefore;
                this.WildcardUnknownAfter           = wildcardAfter;
                this.Search                         = search;
                this.SearchIndex                    = searchIndex;
                this.ContainsCharsBeforeSearchIndex = searchIndex > 0;
                this.ContainsCharsAfterSearchIndex  = searchIndex + search.Length < length;
            }
        }
    }
}
