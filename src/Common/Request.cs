using System;
using System.Net;
using System.Web;
using System.Text;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace Owin {

	public class InvalidRequestException : Exception {
		public InvalidRequestException(string message) : base(message) { }
	}

	public class ParamsDictionary<TKey, TValue> : Dictionary<TKey, TValue>, IDictionary<TKey, TValue> {

		public ParamsDictionary() : base() { }

		public TValue this[TKey key] {
			get {
				try {
					return base[key];
				} catch (KeyNotFoundException) {
					return default(TValue);
				}
			}
			set { base[key] = value; }
		}
	}

	public class Request : IRequest {

		static readonly List<string> FormDataMediaTypes = new List<string> { "application/x-www-form-urlencoded", "multipart/form-data" };

		public IRequest InnerRequest;

		public Request() { }
		public Request(IRequest innerRequest) {
			InnerRequest = innerRequest;
		}

#region IRequest implementation which simply wraps InnerRequest
		// It makes sense for Owin.Request to implement IRequest.
		// We may eventually override some of the IRequest implementation 
		// but, for now, we simply proxy everything to the InnerRequest
		public virtual string Method { get { return InnerRequest.Method; } }
		public virtual string Uri { get { return InnerRequest.Uri; } }
		public virtual IDictionary<string, IEnumerable<string>> Headers { get { return InnerRequest.Headers; } }
		public virtual IDictionary<string, object> Items { get { return InnerRequest.Items; } }
		public virtual IAsyncResult BeginReadBody(byte[] buffer, int offset, int count, AsyncCallback callback, object state) {
			return InnerRequest.BeginReadBody(buffer, offset, count, callback, state);
		}
		public virtual int EndReadBody(IAsyncResult result) { return InnerRequest.EndReadBody(result); }
#endregion

		public virtual string BasePath {
			get { return InnerRequest.Items["owin.base_path"].ToString(); }
		}

		public virtual string Scheme {
			get { return InnerRequest.Items["owin.url_scheme"].ToString(); }
		}

		public virtual string Host {
			get { return StringItemOrNull("owin.server_name"); } // TODO return Host: header if definted?
		}

		public virtual int Port {
			get {
				string port = StringItemOrNull("owin.server_port");
				return (port == null) ? 0 : int.Parse(port);
			}
		}

		public virtual string Protocol {
			get { return StringItemOrNull("owin.request_protocol"); }
		}

		public virtual IPEndPoint IPEndPoint {
			get { return Items["owin.remote_endpoint"] as IPEndPoint; }
		}

		public virtual IPAddress IPAddress {
			get { return IPEndPoint.Address; }
		}

		public virtual string PathInfo {
			get {
				if (Uri.Contains("?"))
					return Uri.Substring(0, Uri.IndexOf("?")); // grab everything before a ?
				else
					return Uri;
			}
		}

		public virtual string Path {
			get { return BasePath + PathInfo; }
		}

		public virtual string Url {
			get {
				string url = Scheme + "://" + Host;

				// If the port is non-standard, include it in the Url
				if ((Scheme == "http" && Port != 80) || (Scheme == "https" && Port != 443))
					url += ":" + Port.ToString();

				url += BasePath + Uri;
				return url;
			}
		}

		/// <summary>Returns the first value of the given header or null if the header does not exist</summary>
		public virtual string GetHeader(string key) {
			key = key.ToLower(); // <--- instead of doing this everywhere, it would be ideal if the Headers IDictionary could do this by itself!
			if (!Headers.ContainsKey(key))
				return null;
			else {
				string value = null;
				foreach (string headerValue in Headers[key]) {
					value = headerValue;
					break;
				}
				return value;
			}
		}

		public virtual string ContentType {
			get { return GetHeader("content-type"); }
		}

		public virtual bool HasFormData {
			get {
				if (FormDataMediaTypes.Contains(ContentType))
					return true;
				else if (Method == "POST" && ContentType == null)
					return true;
				else
					return false;
			}
		}

		public virtual bool IsGet { get { return Method.ToUpper() == "GET"; } }
		public virtual bool IsPost { get { return Method.ToUpper() == "POST"; } }
		public virtual bool IsPut { get { return Method.ToUpper() == "PUT"; } }
		public virtual bool IsDelete { get { return Method.ToUpper() == "DELETE"; } }
		public virtual bool IsHead { get { return Method.ToUpper() == "HEAD"; } }

		/// <summary>Get the raw value of the QueryString</summary>
		/// <remarks>
		/// In OWIN, we include the QueryString in the Uri if it was provided.
		///
		/// This grabs the QueryString from the Uri unless the server provides 
		/// us with a QUERY_STRING.
		/// </remarks>
		public virtual string QueryString {
			get { return new Uri(Url).Query.Replace("?", ""); }
		}

		// alias to Params
		public virtual string this[string key] {
			get { return Params[key]; }
		}

		public virtual IDictionary<string, string> Params {
			get {
				Dictionary<string, string> getAndPost = new ParamsDictionary<string, string>();
				foreach (KeyValuePair<string, string> item in GET)
					getAndPost.Add(item.Key, item.Value);
				foreach (KeyValuePair<string, string> item in POST)
					getAndPost.Add(item.Key, item.Value);
				return getAndPost;
			}
		}

		public virtual IDictionary<string, string> GET {
			get {
				IDictionary<string, string> get = new ParamsDictionary<string, string>();
				NameValueCollection queryStrings = HttpUtility.ParseQueryString(QueryString);
				foreach (string key in queryStrings)
					get.Add(key, queryStrings[key]);
				return get;
			}
		}

		public virtual IDictionary<string, string> POST {
			get {
				IDictionary<string, string> post = new ParamsDictionary<string, string>();
				if (!HasFormData) return post;

				NameValueCollection postVariables = HttpUtility.ParseQueryString(Body);
				foreach (string key in postVariables)
					if (key != null)
						post.Add(key, postVariables[key]);
				return post;
			}
		}

		// blocks while it gets the full body
		public virtual string Body {
			get { return Encoding.UTF8.GetString(BodyBytes); } // TODO should be able to change the encoding used (?)
		}

		// blocks while it gets the full body
		public virtual byte[] BodyBytes {
			get { return GetAllBodyBytes(); }
		}

		public virtual byte[] GetAllBodyBytes() {
			return GetAllBodyBytes(1000); // how many bytes to get per call to BeginReadBody
		}

		public virtual byte[] GetAllBodyBytes(int batchSize) {
			List<byte> allBytes = new List<byte>();
			bool done = false;
			int offset = 0;

			while (!done) {
				byte[] buffer = new byte[batchSize];
				IAsyncResult result = InnerRequest.BeginReadBody(buffer, offset, batchSize, null, null);
				int bytesRead = InnerRequest.EndReadBody(result);

				if (bytesRead == 0)
					done = true;
				else {
					offset += batchSize;
					allBytes.AddRange(buffer);
				}
			}

			return RemoveTrainingBytes(allBytes.ToArray());
		}

		// private

		byte[] RemoveTrainingBytes(byte[] bytes) {
			if (bytes.Length == 0)
				return bytes;

			int i = bytes.Length - 1;
			while (bytes[i] == 0)
				i--;

			if (i == 0)
				return bytes;
			else {
				byte[] newBytes = new byte[i + 1];
				Array.Copy(bytes, newBytes, i + 1);
				return newBytes;
			}
		}

		string StringItemOrNull(string key) {
			if (InnerRequest.Items.ContainsKey(key)) {
				return InnerRequest.Items[key].ToString();
			} else
				return null;
		}
	}
}
