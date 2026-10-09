namespace WhatsInTheGrass;

internal sealed class ModConfig
{
  public bool FadeOverForage { get; set; } = true;
  public int ForageOpacityPercent { get; set; } = 50;
  public bool FadeOverAnimals { get; set; } = true;
  public int AnimalOpacityPercent { get; set; } = 60;
}
