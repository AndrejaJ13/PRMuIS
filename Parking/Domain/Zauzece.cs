using System;

[Serializable]
public class Zauzece
{
    public int BrojParkinga { get; set; }
    public int BrojMesta { get; set; }
    public string VremeNapustanja { get; set; }

    public DateTime VremeDolaska { get; set; } = DateTime.Now;
}