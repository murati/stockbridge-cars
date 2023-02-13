using CefSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stockbridge.Cars.Handlers
{
    public class RenderProcessMessageHandler : IRenderProcessMessageHandler
    {
        public void OnContextReleased(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame)
        {
            //throw new NotImplementedException();
        }

        public void OnFocusedNodeChanged(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IDomNode node)
        {
            //throw new NotImplementedException();
        }

        public void OnUncaughtException(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, JavascriptException exception)
        {
            //throw new NotImplementedException();
        }

        // Wait for the underlying `Javascript Context` to be created, this is only called for the main frame.
        // If the page has no javascript, no context will be created.
        void IRenderProcessMessageHandler.OnContextCreated(IWebBrowser browserControl, IBrowser browser, IFrame frame)
        {
            const string script = "document.addEventListener('DOMContentLoaded', function(){ alert('DomLoaded'); });";

            frame.ExecuteJavaScriptAsync(script);
        }
    }
 
}
