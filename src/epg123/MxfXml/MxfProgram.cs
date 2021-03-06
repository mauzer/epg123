﻿using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace epg123.MxfXml
{
    public class MxfProgram
    {
        private DateTime _oad { get; set; } = DateTime.MinValue;

        [XmlIgnore]
        public DateTime _newDate { get; set; } = DateTime.MinValue;

        [XmlIgnore]
        public int index;

        [XmlIgnore]
        public sdProgram jsonProgramData;

        [XmlIgnore]
        public string md5;

        [XmlIgnore]
        public bool IsPremiere;

        [XmlIgnore]
        public string tmsId { get; set; }

        [XmlIgnore]
        public int _part { get; set; }

        [XmlIgnore]
        public int _parts { get; set; }

        [XmlIgnore]
        public Dictionary<string, string> contentRatings { get; set; }

        [XmlIgnore]
        public string[] contentAdvisories { get; set; }

        [XmlIgnore]
        public string[] genres { get; set; }

        [XmlIgnore]
        public List<string> teams { get; set; }

        [XmlIgnore]
        public IList<sdImage> programImages;

        /// <summary>
        /// A movie or episode.
        /// Example: An episode of Lost, titled "The others strike".
        /// </summary>
        public MxfProgram() { }

        /// <summary>
        /// An ID that is unique to the document and defines this element.
        /// This value should be an integer without a letter prefix.
        /// Program elements are referenced by ScheduleEntry elements.
        /// </summary>
        [XmlAttribute("id")]
        public string Id
        {
            get
            {
                return index.ToString();
            }
            set { }
        }

        /// <summary>
        /// A unique ID that will remain consistent between multiple versions of this document.
        /// This uid should start with "!Program!".
        /// </summary>
        [XmlAttribute("uid")]
        public string Uid
        {
            get
            {
                return ("!Program!" + tmsId.Substring(0, 10) + "_" + tmsId.Substring(10));
            }
            set { }
        }

        /// <summary>
        /// The title of the program (for example, Lost). 
        /// The maximum length is 512 characters.
        /// </summary>
        [XmlAttribute("title")]
        public string Title { get; set; }

        /// <summary>
        /// The episode title of the program (for example, The others attack).
        /// The maximum length is 512 characters.
        /// </summary>
        [XmlAttribute("episodeTitle")]
        public string EpisodeTitle { get; set; }

        /// <summary>
        /// The description of this program.
        /// The maximum length is 2048 characters.
        /// </summary>
        [XmlAttribute("description")]
        public string Description { get; set; }

        /// <summary>
        /// A shorter form of the description attribute, if available.
        /// The maximum length is 512 characters. If a short description is not available, do not specify a value.
        /// </summary>
        [XmlAttribute("shortDescription")]
        public string ShortDescription { get; set; }

        /// <summary>
        /// The language of the program.
        /// </summary>
        [XmlAttribute("language")]
        public string Language { get; set; }

        /// <summary>
        /// The year the program was created.
        /// If unknown, this value is 0.
        /// </summary>
        [XmlAttribute("year")]
        public int Year { get; set; } = 0;
        public bool ShouldSerializeYear() { return Year > 0; }

        /// <summary>
        /// The season number of the program (for example, 1). 
        /// If unknown, this value is 0.
        /// </summary>
        [XmlAttribute("seasonNumber")]
        public int SeasonNumber { get; set; } = 0;
        public bool ShouldSerializeSeasonNumber() { return SeasonNumber > 0; }

        /// <summary>
        /// The episode number of the program in the season.
        /// If unknown, this value is 0.
        /// </summary>
        [XmlAttribute("episodeNumber")]
        public int EpisodeNumber { get; set; } = 0;
        public bool ShouldSerializeEpisodeNumber() { return EpisodeNumber > 0; }

        /// <summary>
        /// The original air date (in local time) of the program. Use this value to determine whether this program is a repeat.
        /// </summary>
        [XmlAttribute("originalAirdate")]
        public string OriginalAirdate
        {
            get
            {
                if (!IsGeneric && _oad != DateTime.MinValue && _newDate != DateTime.MinValue)
                {
                    return _newDate.ToString("yyyy-MM-dd");
                }
                else if (_oad != DateTime.MinValue)
                {
                    return _oad.ToString("yyyy-MM-dd");
                }
                else return null;
            }
            set
            {
                _oad = DateTime.Parse(value);
            }
        }

        /// <summary>
        /// A comma-delimited list of keyword IDs. This value specifies the Keyword attributes that this program has.
        /// </summary>
        [XmlAttribute("keywords")]
        public string Keywords { get; set; }

        /// <summary>
        /// The ID of the season that this program belongs to, if any.
        /// If this value is not known, do not specify a value.
        /// </summary>
        [XmlAttribute("season")]
        public string Season { get; set; }

        /// <summary>
        /// The ID of the series that this program belongs to, if any.
        /// If this value is not known, do not specify a value.
        /// </summary>
        [XmlAttribute("series")]
        public string Series { get; set; }

        /// <summary>
        /// The star rating of the program. 
        /// Each star equals two points. For example, a value of "3" is equal to 1.5 stars.
        /// </summary>
        [XmlAttribute("halfStars")]
        public int HalfStars { get; set; } = 0;
        public bool ShouldSerializeHalfStars() { return HalfStars > 0; }

        /// <summary>
        /// The MPAA movie rating.
        /// Possible values are:
        /// 0 = Unknown
        /// 1 = G
        /// 2 = PG
        /// 3 = PG13
        /// 4 = R
        /// 5 = NC17
        /// 6 = X
        /// 7 = NR
        /// 8 = AO
        /// </summary>
        [XmlAttribute("mpaaRating")]
        public int MpaaRating { get; set; } = 0;
        public bool ShouldSerializeMpaaRating() { return MpaaRating > 0; }

        /// <summary>
        /// Undocumented
        /// </summary>
        [XmlAttribute("isAction")]
        public bool IsAction { get; set; } = false;
        public bool ShouldSerializeIsAction() { return IsAction; }

        [XmlIgnore]
        public bool IsAdultOnly { get; set; } = false;

        /// <summary>
        /// Undocumented
        /// </summary>
        [XmlAttribute("isComedy")]
        public bool IsComedy { get; set; } = false;
        public bool ShouldSerializeIsComedy() { return IsComedy; }

        /// <summary>
        /// Undocumented
        /// </summary>
        [XmlAttribute("isDocumentary")]
        public bool IsDocumentary { get; set; } = false;
        public bool ShouldSerializeIsDocumentary() { return IsDocumentary; }

        /// <summary>
        /// Undocumented
        /// </summary>
        [XmlAttribute("isDrama")]
        public bool IsDrama { get; set; } = false;
        public bool ShouldSerializeIsDrama() { return IsDrama; }

        /// <summary>
        /// Undocumented
        /// </summary>
        [XmlAttribute("isEducational")]
        public bool IsEducational { get; set; } = false;
        public bool ShouldSerializeIsEducational() { return IsEducational; }

        /// <summary>
        /// Undocumented
        /// </summary>
        [XmlAttribute("isHorror")]
        public bool IsHorror { get; set; } = false;
        public bool ShouldSerializeIsHorror() { return IsHorror; }

        /// <summary>
        /// Undocumented
        /// </summary>
        [XmlAttribute("isIndy")]
        public bool IsIndy { get; set; } = false;
        public bool ShouldSerializeIsIndy() { return IsIndy; }

        /// <summary>
        /// Undocumented
        /// </summary>
        [XmlAttribute("isMusic")]
        public bool IsMusic { get; set; } = false;
        public bool ShouldSerializeIsMusic() { return IsMusic; }

        /// <summary>
        /// Undocumented
        /// </summary>
        [XmlAttribute("isRomance")]
        public bool IsRomance { get; set; } = false;
        public bool ShouldSerializeIsRomance() { return IsRomance; }

        /// <summary>
        /// Undocumented
        /// </summary>
        [XmlAttribute("isScienceFiction")]
        public bool IsScienceFiction { get; set; } = false;
        public bool ShouldSerializeIsScienceFiction() { return IsScienceFiction; }

        /// <summary>
        /// Undocumented
        /// </summary>
        [XmlAttribute("isSoap")]
        public bool IsSoap { get; set; } = false;
        public bool ShouldSerializeIsSoap() { return IsSoap; }

        /// <summary>
        /// Undocumented
        /// </summary>
        [XmlAttribute("isThriller")]
        public bool IsThriller { get; set; } = false;
        public bool ShouldSerializeIsThriller() { return IsThriller; }

        /// <summary>
        /// Undocumented
        /// </summary>
        [XmlAttribute("isProgramEpisodic")]
        public bool IsProgramEpisodic { get; set; } = false;
        public bool ShouldSerializeIsProgramEpisodic() { return IsProgramEpisodic; }

        /// <summary>
        /// Indicates whether the program is a movie.
        /// This value determines whether the program appears in the Movies category of the Guide grid and in other movie-related locations.
        /// </summary>
        [XmlAttribute("isMovie")]
        public bool IsMovie { get; set; } = false;
        public bool ShouldSerializeIsMovie() { return IsMovie; }

        /// <summary>
        /// Indicates whether the program is a miniseries.
        /// </summary>
        [XmlAttribute("isMiniseries")]
        public bool IsMiniseries { get; set; } = false;
        public bool ShouldSerializeIsMiniseries() { return IsMiniseries; }

        /// <summary>
        /// Indicates whether the program is a limited series.
        /// </summary>
        [XmlAttribute("isLimitedSeries")]
        public bool IsLimitedSeries { get; set; } = false;
        public bool ShouldSerializeIsLimitedSeries() { return IsLimitedSeries; }

        /// <summary>
        /// Indicates whether the program is paid programming.
        /// </summary>
        [XmlAttribute("isPaidProgramming")]
        public bool IsPaidProgramming { get; set; } = false;
        public bool ShouldSerializeIsPaidProgramming() { return IsPaidProgramming; }

        /// <summary>
        /// Indicates whether the program is a serial.
        /// </summary>
        [XmlAttribute("isSerial")]
        public bool IsSerial { get; set; } = false;
        public bool ShouldSerializeIsSerial() { return IsSerial; }

        /// <summary>
        /// Indicates whether the program is part of a series.
        /// </summary>
        [XmlAttribute("isSeries")]
        public bool IsSeries { get; set; } = false;
        public bool ShouldSerializeIsSeries() { return IsSeries; }

        /// <summary>
        /// Undocumented
        /// </summary>
        [XmlAttribute("isSeasonPremiere")]
        public bool IsSeasonPremiere { get; set; } = false;
        public bool ShouldSerializeIsSeasonPremiere() { return IsSeasonPremiere; }

        /// <summary>
        /// Undocumented
        /// </summary>
        [XmlAttribute("isSeasonFinale")]
        public bool IsSeasonFinale { get; set; } = false;
        public bool ShouldSerializeIsSeasonFinale() { return IsSeasonFinale; }

        /// <summary>
        /// Undocumented
        /// </summary>
        [XmlAttribute("isSeriesPremiere")]
        public bool IsSeriesPremiere { get; set; } = false;
        public bool ShouldSerializeIsSeriesPremiere() { return IsSeriesPremiere; }

        /// <summary>
        /// Undocumented
        /// </summary>
        [XmlAttribute("isSeriesFinale")]
        public bool IsSeriesFinale { get; set; } = false;
        public bool ShouldSerializeIsSeriesFinale() { return IsSeriesFinale; }

        /// <summary>
        /// Indicates whether the program is a short film.
        /// </summary>
        [XmlAttribute("isShortFilm")]
        public bool IsShortFilm { get; set; } = false;
        public bool ShouldSerializeIsShortFilm() { return IsShortFilm; }

        /// <summary>
        /// Indicates whether the program is a special.
        /// This value is used in the Guide's grid view categories.
        /// </summary>
        [XmlAttribute("isSpecial")]
        public bool IsSpecial { get; set; } = false;
        public bool ShouldSerializeIsSpecial() { return IsSpecial; }

        /// <summary>
        /// Indicates whether the program is a sports program.
        /// This value is used in the Guide's grid view categories.
        /// </summary>
        [XmlAttribute("isSports")]
        public bool IsSports { get; set; } = false;
        public bool ShouldSerializeIsSports() { return IsSports; }

        /// <summary>
        /// Indicates whether the program is a news show.
        /// This value is used in the Guide's grid view categories.
        /// </summary>
        [XmlAttribute("isNews")]
        public bool IsNews { get; set; } = false;
        public bool ShouldSerializeIsNews() { return IsNews; }

        /// <summary>
        /// Indicates whether the program is for children.
        /// This value is used in the Guide's grid view categories.
        /// </summary>
        [XmlAttribute("isKids")]
        public bool IsKids { get; set; } = false;
        public bool ShouldSerializeIsKids() { return IsKids; }

        /// <summary>
        /// Indicates whether the program is a reality show.
        /// </summary>
        [XmlAttribute("isReality")]
        public bool IsReality { get; set; } = false;
        public bool ShouldSerializeIsReality() { return IsReality; }

        /// <summary>
        /// Indicates program is soft/contains generic description.
        /// Use for series episodes that are not known yet.
        /// </summary>
        [XmlAttribute("isGeneric")]
        public bool IsGeneric { get; set; } = false;
        public bool ShouldSerializeIsGeneric() { return IsGeneric; }

        /// <summary>
        /// This value indicates the reason for the MPAA rating.
        /// </summary>
        [XmlAttribute("hasAdult")]
        public bool HasAdult { get; set; } = false;
        public bool ShouldSerializeHasAdult() { return HasAdult; }

        /// <summary>
        /// This value indicates the reason for the MPAA rating.
        /// </summary>
        [XmlAttribute("hasBriefNudity")]
        public bool HasBriefNudity { get; set; } = false;
        public bool ShouldSerializeHasBriefNudity() { return HasBriefNudity; }

        /// <summary>
        /// This value indicates the reason for the MPAA rating.
        /// </summary>
        [XmlAttribute("hasGraphicLanguage")]
        public bool HasGraphicLanguage { get; set; } = false;
        public bool ShouldSerializeHasGraphicLanguage() { return HasGraphicLanguage; }

        /// <summary>
        /// This value indicates the reason for the MPAA rating.
        /// </summary>
        [XmlAttribute("hasGraphicViolence")]
        public bool HasGraphicViolence { get; set; } = false;
        public bool ShouldSerializeHasGraphicViolence() { return HasGraphicViolence; }

        /// <summary>
        /// This value indicates the reason for the MPAA rating.
        /// </summary>
        [XmlAttribute("hasLanguage")]
        public bool HasLanguage { get; set; } = false;
        public bool ShouldSerializeHasLanguage() { return HasLanguage; }

        /// <summary>
        /// This value indicates the reason for the MPAA rating.
        /// </summary>
        [XmlAttribute("hasMildViolence")]
        public bool HasMildViolence { get; set; } = false;
        public bool ShouldSerializeHasMildViolence() { return HasMildViolence; }

        /// <summary>
        /// This value indicates the reason for the MPAA rating.
        /// </summary>
        [XmlAttribute("hasNudity")]
        public bool HasNudity { get; set; } = false;
        public bool ShouldSerializeHasNudity() { return HasNudity; }

        /// <summary>
        /// This value indicates the reason for the MPAA rating.
        /// </summary>
        [XmlAttribute("hasRape")]
        public bool HasRape { get; set; } = false;
        public bool ShouldSerializeHasRape() { return HasRape; }

        /// <summary>
        /// This value indicates the reason for the MPAA rating.
        /// </summary>
        [XmlAttribute("hasStrongSexualContent")]
        public bool HasStrongSexualContent { get; set; } = false;
        public bool ShouldSerializeHasStrongSexualContent() { return HasStrongSexualContent; }

        /// <summary>
        /// This value indicates the reason for the MPAA rating.
        /// </summary>
        [XmlAttribute("hasViolence")]
        public bool HasViolence { get; set; } = false;
        public bool ShouldSerializeHasViolence() { return HasViolence; }

        /// <summary>
        /// This value contains an image to display for the program.
        /// Contains the value of a GuideImage id attribute. When a program is selected in the UI, the Guide searches for an image to display.The search order is first the program, its season, then its series.
        /// </summary>
        [XmlAttribute("guideImage")]
        public string GuideImage { get; set; }

        [XmlElement("ActorRole")]
        public List<MxfPersonRank> ActorRole { get; set; }

        [XmlElement("WriterRole")]
        public List<MxfPersonRank> WriterRole { get; set; }

        [XmlElement("GuestActorRole")]
        public List<MxfPersonRank> GuestActorRole { get; set; }

        [XmlElement("HostRole")]
        public List<MxfPersonRank> HostRole { get; set; }

        [XmlElement("ProducerRole")]
        public List<MxfPersonRank> ProducerRole { get; set; }

        [XmlElement("DirectorRole")]
        public List<MxfPersonRank> DirectorRole { get; set; }
    }
}
