using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace FanSelector.Core
{
    /// <summary>
    /// Which parameter of one fan family carries which performance figure.
    ///
    /// This is the whole reason the add-in works with anybody's families: it
    /// stores parameter NAMES chosen by the user, so nothing about a particular
    /// manufacturer's naming is compiled in.
    /// </summary>
    [DataContract]
    internal class FamilyMapping
    {
        [DataMember(Name = "family", Order = 0)]
        public string FamilyName { get; set; }

        [DataMember(Name = "airFlow", Order = 1)]
        public string AirFlow { get; set; }

        [DataMember(Name = "pressure", Order = 2)]
        public string Pressure { get; set; }

        [DataMember(Name = "power", Order = 3)]
        public string Power { get; set; }

        [DataMember(Name = "speed", Order = 4)]
        public string Speed { get; set; }

        [DataMember(Name = "sfp", Order = 5)]
        public string Sfp { get; set; }

        [DataMember(Name = "soundPower", Order = 6)]
        public string SoundPower { get; set; }

        /// <summary>
        /// Extra type parameters shown as further columns in the results grid.
        /// They take no part in filtering — they are there because the person
        /// choosing a fan wants to see them next to the numbers.
        /// </summary>
        [DataMember(Name = "extraColumns", Order = 7)]
        public List<string> ExtraColumns { get; set; }

        /// <summary>
        /// Optional INSTANCE parameter the selected air flow is written to after
        /// placement. Families that drive a connector from an instance override
        /// need this; families that read air flow off the type do not.
        /// </summary>
        [DataMember(Name = "instanceAirFlow", Order = 8)]
        public string InstanceAirFlow { get; set; }

        public FamilyMapping()
        {
            ExtraColumns = new List<string>();
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (ExtraColumns == null) ExtraColumns = new List<string>();
        }

        public string Get(FanQuantity quantity)
        {
            switch (quantity)
            {
                case FanQuantity.AirFlow: return AirFlow;
                case FanQuantity.Pressure: return Pressure;
                case FanQuantity.Power: return Power;
                case FanQuantity.Speed: return Speed;
                case FanQuantity.Sfp: return Sfp;
                case FanQuantity.SoundPower: return SoundPower;
                default: throw new ArgumentOutOfRangeException("quantity");
            }
        }

        public void Set(FanQuantity quantity, string parameterName)
        {
            string value = string.IsNullOrEmpty(parameterName) ? null : parameterName;
            switch (quantity)
            {
                case FanQuantity.AirFlow: AirFlow = value; break;
                case FanQuantity.Pressure: Pressure = value; break;
                case FanQuantity.Power: Power = value; break;
                case FanQuantity.Speed: Speed = value; break;
                case FanQuantity.Sfp: Sfp = value; break;
                case FanQuantity.SoundPower: SoundPower = value; break;
                default: throw new ArgumentOutOfRangeException("quantity");
            }
        }

        public bool IsUsable
        {
            get
            {
                return !string.IsNullOrEmpty(FamilyName)
                    && !string.IsNullOrEmpty(AirFlow)
                    && !string.IsNullOrEmpty(Pressure);
            }
        }

        public FamilyMapping Clone()
        {
            return new FamilyMapping
            {
                FamilyName = FamilyName,
                AirFlow = AirFlow,
                Pressure = Pressure,
                Power = Power,
                Speed = Speed,
                Sfp = Sfp,
                SoundPower = SoundPower,
                InstanceAirFlow = InstanceAirFlow,
                ExtraColumns = new List<string>(ExtraColumns ?? new List<string>())
            };
        }

        public override string ToString()
        {
            return FamilyName ?? "(no family)";
        }
    }
}
