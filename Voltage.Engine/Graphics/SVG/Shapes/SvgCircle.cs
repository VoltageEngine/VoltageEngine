using System.Xml.Serialization;


namespace Voltage.Svg
{
	public class SvgCircle : SvgElement
	{
		[XmlAttribute("r")] public float Radius;

		[XmlAttribute("cy")] public float CenterY;

		[XmlAttribute("cx")] public float CenterX;
	}
}