using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Web;
using System.Text;

namespace Owin.Handlers
{
    public class Wcf
    {
        public static void Run(IApplication application)
        {
            var wcfApp = new WcfOwinHost(application, new RequestFactory(), new ResponseMessageFactory());
            var host = new WebServiceHost(wcfApp, new Uri("http://localhost:8080"));
            host.Open();
            Console.WriteLine("WcfServer is running at http://localhost:8080/");
            Console.WriteLine("Press any key to exit");
            Console.Read();
        }
    }

    public interface IRequestFactory
    {
        IRequest Create(Stream body);
    }

    public class RequestFactory : IRequestFactory
    {
        public IRequest Create(Stream body)
        {
            var wcfRequest = WebOperationContext.Current.IncomingRequest;
            var writer = new RequestWriter(wcfRequest.Method, wcfRequest.UriTemplateMatch.RequestUri.AbsoluteUri);
            var buffer = new byte[body.Length];
            int i = body.Read(buffer, 0, (int)body.Length);
            writer.SetBody(buffer);
            foreach (string header in wcfRequest.Headers.Keys)
            {
                writer.AddHeader(header, wcfRequest.Headers[header]);
            }

            var request = writer.InnerRequest;
            SetItems(request.Items, WebOperationContext.Current.IncomingRequest.UriTemplateMatch);
            return request;
        }

        void SetItems(IDictionary<string, object> items, UriTemplateMatch requestTemplateMatch)
        {

            items["owin.base_path"] = requestTemplateMatch.BaseUri;
            items["owin.server_name"] = requestTemplateMatch.RequestUri.Host;
            items["owin.server_port"] = requestTemplateMatch.RequestUri.Port;
            items["owin.request_protocol"] = "HTTP/1.1";
            items["owin.url_scheme"] = requestTemplateMatch.RequestUri.Scheme;

            OperationContext context = OperationContext.Current;
            

            var prop = context.IncomingMessageProperties;
            var endpoint = prop[RemoteEndpointMessageProperty.Name] as RemoteEndpointMessageProperty;
            var ip = new IPEndPoint(IPAddress.Parse(endpoint.Address), requestTemplateMatch.RequestUri.Port);
            items["owin.remote_endpont"] = ip;
        }
    }

    public interface IResponseMessageFactory
    {
        Message Create(IResponse response);
    }

    public class ResponseMessageFactory : IResponseMessageFactory
    {
        public Message Create(IResponse response)
        {
            var wcfResponse = WebOperationContext.Current.OutgoingResponse;
            foreach (var header in response.Headers)
            {
                foreach (var value in header.Value)
                {
                    wcfResponse.Headers.Add(header.Key, value);
                }
            }
            var message = WebOperationContext.Current.CreateStreamResponse(s => WriteBody(s, response), response.Headers["content-type"].FirstOrDefault());
            return message;
        }

        private void WriteBody(Stream stream, IResponse response)
        {
            var writer = new StreamWriter(stream);
            foreach(var item in response.GetBody())
            {
                if (item is FileInfo)
                {
                    //tbd
                }
                else if(item is byte[])
                {
                    var buffer = item as byte[];
                    stream.Write(buffer, 0, buffer.Length);
                }
                else if (item is ArraySegment<byte>)
                {
                    var buffer = (ArraySegment<byte>) item;
                    stream.Write(buffer.Array, 0, buffer.Count);
                }
                else
                {
                    var buffer = (string) item;
                    writer.Write(buffer);
                }
            }
            writer.Flush();
            stream.Flush();
        }
    }


    [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
    [ServiceContract]
    public class WcfOwinHost
    {
        private IApplication _application;
        private IRequestFactory _requestFactory;
        private IResponseMessageFactory _responseFactory;

        public WcfOwinHost(IApplication application, IRequestFactory requestFactory, IResponseMessageFactory responseFactory)
        {
            _responseFactory = responseFactory;
            _requestFactory = requestFactory;
            _application = application;
        }

        [OperationContract(AsyncPattern = true)]
        [WebInvoke(UriTemplate = "*", Method = "*")]
        public IAsyncResult BeginHandleAsync(Stream body, AsyncCallback callback, object state)
        {
            var request = _requestFactory.Create(body);
            return _application.BeginInvoke(request, callback, state);
        }

        public Message EndHandleAsync(IAsyncResult r)
        {
            var response = _application.EndInvoke(r);
            return _responseFactory.Create(response);
        }
    }
}
