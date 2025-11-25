namespace Voltage.AI.UtilityAI
{
	public interface IAction<T>
	{
		void Execute(T context);
	}
}