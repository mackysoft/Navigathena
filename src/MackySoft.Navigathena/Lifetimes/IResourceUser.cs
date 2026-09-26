using System.Threading.Tasks;

namespace MackySoft.Navigathena
{
    internal interface IResourceUser
    {
        ValueTask RequestEndAsync ();
    }
}
