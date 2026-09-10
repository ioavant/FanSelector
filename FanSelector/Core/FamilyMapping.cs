using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Autodesk.Revit.DB;

namespace FanSelector.Core
{
    /// <summary>
    /// How one fan family is wired up, in two halves.
    ///
    /// READ — which column of the type catalogue (.csv) carries each figure. That
    /// file is the performance database: it is the same file Revit reads to decide
    /// which types the family offers, so the two can never drift apart.
    ///
    /// WRITE — which parameter of the family each figure should be written to when
    /// a fan is placed. Optional, and needed because fan families normally keep
    /// these as instance parameters that hold no value until something fills them.
    ///
    /// Both halves are names chosen by the user, so no manufacturer's naming is
    /// compiled into the add-in.
    /// </summary>
    [DataContract]
    internal class FamilyMapping
    {
        [DataMember(Name = "family", Order = 0)]
        public string FamilyName { get; set; }

        /// <summary>
        /// Full path to the type catalogue. The family's .rfa is expected beside it
        /// under the same name, which is what Revit itself requires of a catalogue.
        /// </summary>
        [DataMember(Name = "catalog", Order = 1)]
        public string CatalogPath { get; set; }

        // ── Catalogue columns the figures are read from ────────────────────────

        [DataMember(Name = "airFlowColumn", Order = 2)] public string AirFlowColumn { get; set; }
        [DataMember(Name = "pressureColumn", Order = 3)] public string PressureColumn { get; set; }
        [DataMember(Name = "powerColumn", Order = 4)] public string PowerColumn { get; set; }
        [DataMember(Name = "speedColumn", Order = 5)] public string SpeedColumn { get; set; }
        [DataMember(Name = "sfpColumn", Order = 6)] public string SfpColumn { get; set; }
        [DataMember(Name = "soundPowerColumn", Order = 7)] public string SoundPowerColumn { get; set; }

        // ── Family parameters the figures are written to ───────────────────────

        [DataMember(Name = "airFlowParam", Order = 8)] public string AirFlowParam { get; set; }
        [DataMember(Name = "pressureParam", Order = 9)] public string PressureParam { get; set; }
        [DataMember(Name = "powerParam", Order = 10)] public string PowerParam { get; set; }
        [DataMember(Name = "speedParam", Order = 11)] public string SpeedParam { get; set; }
        [DataMember(Name = "sfpParam", Order = 12)] public string SfpParam { get; set; }
        [DataMember(Name = "soundPowerParam", Order = 13)] public string SoundPowerParam { get; set; }

        /// <summary>
        /// Extra catalogue columns shown as further columns in the results grid.
        /// They take no part in filtering — they are there because the person
        /// choosing a fan wants to see them next to the numbers.
        /// </summary>
        [DataMember(Name = "extraColumns", Order = 14)]
        public List<string> ExtraColumns { get; set; }

        // The picture shown beside the results is NOT stored here. It follows from
        // the family's name — see FanImages — because the four pictures shipped
        // with the add-in cover the shapes a fan comes in and the answer is always
        // the same for a given name.

        // ── Optional duct stub and closer ──────────────────────────────────────

        /// <summary>
        /// Whether placing a fan of this family also grows a short duct off one
        /// connector and caps it with an air terminal carrying the requested air
        /// flow. Per family rather than a global switch: it makes sense for an
        /// axial fan sitting in a duct run and not for every fan.
        /// </summary>
        [DataMember(Name = "addDuctStub", Order = 16)]
        public bool AddDuctStub { get; set; }

        /// <summary>Air terminal family used to cap the stub.</summary>
        [DataMember(Name = "closerFamily", Order = 17)]
        public string CloserFamily { get; set; }

        /// <summary>Length of the stub in millimetres.</summary>
        [DataMember(Name = "stubLengthMm", Order = 18)]
        public double StubLengthMm { get; set; }

        /// <summary>Default stub length: 10 mm, i.e. the 1 cm the stub is meant to be.</summary>
        public const double DefaultStubLengthMm = 10.0;

        public const string DefaultCloserFamily = "AT_Fan Closer";

        public FamilyMapping()
        {
            ExtraColumns = new List<string>();
            StubLengthMm = DefaultStubLengthMm;
            CloserFamily = DefaultCloserFamily;
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (ExtraColumns == null) ExtraColumns = new List<string>();
            if (StubLengthMm <= 0.0) StubLengthMm = DefaultStubLengthMm;
            if (string.IsNullOrEmpty(CloserFamily)) CloserFamily = DefaultCloserFamily;
        }

        /// <summary>The stub length in Revit's internal units.</summary>
        public double StubLengthFt
        {
            get
            {
                try { return UnitUtils.ConvertToInternalUnits(StubLengthMm, UnitTypeId.Millimeters); }
                catch { return 0.0; }
            }
        }

        public void SetStubLengthFt(double feet)
        {
            try { StubLengthMm = UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters); }
            catch { StubLengthMm = DefaultStubLengthMm; }
        }

        public string Column(FanQuantity quantity)
        {
            switch (quantity)
            {
                case FanQuantity.AirFlow: return AirFlowColumn;
                case FanQuantity.Pressure: return PressureColumn;
                case FanQuantity.Power: return PowerColumn;
                case FanQuantity.Speed: return SpeedColumn;
                case FanQuantity.Sfp: return SfpColumn;
                case FanQuantity.SoundPower: return SoundPowerColumn;
                default: throw new ArgumentOutOfRangeException("quantity");
            }
        }

        public void SetColumn(FanQuantity quantity, string columnName)
        {
            string value = string.IsNullOrEmpty(columnName) ? null : columnName;
            switch (quantity)
            {
                case FanQuantity.AirFlow: AirFlowColumn = value; break;
                case FanQuantity.Pressure: PressureColumn = value; break;
                case FanQuantity.Power: PowerColumn = value; break;
                case FanQuantity.Speed: SpeedColumn = value; break;
                case FanQuantity.Sfp: SfpColumn = value; break;
                case FanQuantity.SoundPower: SoundPowerColumn = value; break;
                default: throw new ArgumentOutOfRangeException("quantity");
            }
        }

        public string Param(FanQuantity quantity)
        {
            switch (quantity)
            {
                case FanQuantity.AirFlow: return AirFlowParam;
                case FanQuantity.Pressure: return PressureParam;
                case FanQuantity.Power: return PowerParam;
                case FanQuantity.Speed: return SpeedParam;
                case FanQuantity.Sfp: return SfpParam;
                case FanQuantity.SoundPower: return SoundPowerParam;
                default: throw new ArgumentOutOfRangeException("quantity");
            }
        }

        public void SetParam(FanQuantity quantity, string parameterName)
        {
            string value = string.IsNullOrEmpty(parameterName) ? null : parameterName;
            switch (quantity)
            {
                case FanQuantity.AirFlow: AirFlowParam = value; break;
                case FanQuantity.Pressure: PressureParam = value; break;
                case FanQuantity.Power: PowerParam = value; break;
                case FanQuantity.Speed: SpeedParam = value; break;
                case FanQuantity.Sfp: SfpParam = value; break;
                case FanQuantity.SoundPower: SoundPowerParam = value; break;
                default: throw new ArgumentOutOfRangeException("quantity");
            }
        }

        public bool IsUsable
        {
            get
            {
                return !string.IsNullOrEmpty(FamilyName)
                    && !string.IsNullOrEmpty(CatalogPath)
                    && !string.IsNullOrEmpty(AirFlowColumn)
                    && !string.IsNullOrEmpty(PressureColumn);
            }
        }

        public FamilyMapping Clone()
        {
            return new FamilyMapping
            {
                FamilyName = FamilyName,
                CatalogPath = CatalogPath,
                AirFlowColumn = AirFlowColumn,
                PressureColumn = PressureColumn,
                PowerColumn = PowerColumn,
                SpeedColumn = SpeedColumn,
                SfpColumn = SfpColumn,
                SoundPowerColumn = SoundPowerColumn,
                AirFlowParam = AirFlowParam,
                PressureParam = PressureParam,
                PowerParam = PowerParam,
                SpeedParam = SpeedParam,
                SfpParam = SfpParam,
                SoundPowerParam = SoundPowerParam,
                ExtraColumns = new List<string>(ExtraColumns ?? new List<string>()),
                AddDuctStub = AddDuctStub,
                CloserFamily = CloserFamily,
                StubLengthMm = StubLengthMm
            };
        }

        public override string ToString()
        {
            return FamilyName ?? "(no family)";
        }
    }
}
