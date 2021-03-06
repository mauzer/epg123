﻿using System.Collections.Generic;
using System.Xml;
using System.Xml.Serialization;

namespace epg123
{
    [XmlRoot("CustomLineups")]
    public class CustomLineups
    {
        [XmlElement("CustomLineup")]
        public List<CustomLineup> CustomLineup { get; set; }
    }

    public class CustomLineup
    {
        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlAttribute("location")]
        public string Location { get; set; }

        [XmlAttribute("lineup")]
        public string Lineup { get; set; }

        [XmlElement("station")]
        public List<CustomStation> Station { get; set; }
    }

    public class CustomStation
    {
        [XmlAttribute("number")]
        public int Number { get; set; }

        [XmlAttribute("subnumber")]
        public int Subnumber { get; set; }

        [XmlAttribute("callsign")]
        public string Callsign { get; set; }

        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlAttribute("matchName")]
        public string MatchName { get; set; }

        [XmlAttribute("alternate")]
        public string Alternate { get; set; }

        [XmlText()]
        public string StationId { get; set; }
    }
}
