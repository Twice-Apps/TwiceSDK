namespace Playfab_Interface_Currency
{
    public interface ICurrency
    {
        void Get();
        void Add(int amount);
        void Substract(int amount);

        string CurrencyKey();
    }
}