namespace MackySoft.Navigathena.Runtime.Execution
{

    internal sealed class NavigationOperationContext
    {
        public NavigationOperationContext (NavigationOperationId operationId, NavigationRequest request)
        {
            OperationId = operationId;
            Request = request;
            Diagnostics = new OperationDiagnostics();
            Progress = new RuntimeProgressReporter(operationId, request.Options?.Progress, Diagnostics);
        }

        public NavigationOperationId OperationId
        {
            get;
        }
        public NavigationRequest Request
        {
            get;
        }
        public OperationDiagnostics Diagnostics
        {
            get;
        }
        public RuntimeProgressReporter Progress
        {
            get;
        }
    }

}
