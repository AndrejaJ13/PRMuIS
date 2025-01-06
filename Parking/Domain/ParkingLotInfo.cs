using System;

namespace Domain
{
    [Serializable]
    public class ParkingLotInfo
    {
        public int TotalSpaces { get; set; }
        public int OccupiedSpaces { get; set; }
        public decimal PricePerHour { get; set; }
    }
}
