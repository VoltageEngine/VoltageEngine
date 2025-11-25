namespace Voltage
{
	public interface IClipboard
	{
		string GetContents();
		void SetContents(string text);
	}
}