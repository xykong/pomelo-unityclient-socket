using System.Collections;
using SimpleJson;

using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;

namespace Pomelo.DotNetClient {
	public class PomeloClient : IDisposable {
		public const string EVENT_DISCONNECT = "disconnect";

		private EventManager eventManager;
		private Socket socket;
		private Protocol protocol;
		private bool disposed = false;
		private uint reqId = 1;

		private Action<JsonObject> connectCallback;
		private Queue<Message> receiveQueues = new Queue<Message>();

		public PomeloClient(string host, int port) {
			this.eventManager = new EventManager();
			initClient(host, port);
		}

		public PomeloClient(string host, int port, Action<JsonObject> inConnectCallback) {
			connectCallback = inConnectCallback;
			this.eventManager = new EventManager();
			initClient(host, port);
		}

		public Pomelo.DotNetClient.ProtocolState State {
			get { return protocol.State; }
		}

		private void initClient(string host, int port) {
			this.socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			this.socket.SendBufferSize = 512 * 1024;
			this.socket.ReceiveBufferSize = 512 * 1024;
			IPEndPoint ie = new IPEndPoint(IPAddress.Parse(host), port);
			this.protocol = new Protocol(this, socket);
			try {
				if (this.connectCallback == null) {
					this.socket.Connect(ie);
				}
				else {
					this.socket.BeginConnect(ie, new AsyncCallback(internalConnectCallback), this.socket);
				}
			}
			catch (SocketException e) {
				Console.WriteLine(String.Format("unable to connect to server: {0}", e.ToString()));
				this.protocol.State = ProtocolState.closed;
			}
		}

		private void internalConnectCallback(IAsyncResult ar) {
			try {
				Socket client = (Socket)ar.AsyncState;
				client.EndConnect(ar);
			}
			catch {
				this.protocol.State = ProtocolState.closed;
			}

			if (this.connectCallback != null) {
				this.connectCallback(null);
			}
		}

		public void connect() {
			protocol.start(null, null);
		}

		public void connect(JsonObject user) {
			protocol.start(user, null);
		}

		public void connect(Action<JsonObject> handshakeCallback) {
			protocol.start(null, handshakeCallback);
		}

		public bool connect(JsonObject user, Action<JsonObject> handshakeCallback) {
			try {
				protocol.start(user, handshakeCallback);
				return true;
			}catch(Exception e){
				Console.WriteLine(e.ToString());
				return false;
			}
		}

		public void request(string route, Action<JsonObject> action) {
			this.request(route, new JsonObject(), action);
		}

		public void request(string route, JsonObject msg, Action<JsonObject> action) {
			this.eventManager.AddCallBack(reqId, action);
			protocol.send(route, reqId, msg);

			reqId++;
		}

		public void notify(string route, JsonObject msg) {
			protocol.send(route, msg);
		}

		public void on(string eventName, Action<JsonObject> action) {
			eventManager.AddOnEvent(eventName, action);
		}

		internal void processMessage(Message msg) {
			pushMessage(msg);
		}

		private void pushMessage(Message msg) {
			receiveQueues.Enqueue(msg);
		}

		public void popMessage() {
			if (receiveQueues.Count != 0) {
				Message msg = receiveQueues.Dequeue();

				if (msg.type == MessageType.MSG_RESPONSE) {
					eventManager.InvokeCallBack(msg.id, msg.data);
				}
				else if (msg.type == MessageType.MSG_PUSH) {
					eventManager.InvokeOnEvent(msg.route, msg.data);
				}
			}
		}

		public void disconnect() {
			Dispose();
		}

		public void Dispose() {
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		// The bulk of the clean-up code 
		protected virtual void Dispose(bool disposing) {
			if (this.disposed) return;

			if (disposing) {
				// free managed resources
				this.protocol.close();
				this.socket.Shutdown(SocketShutdown.Both);
				this.socket.Close();
				this.disposed = true;

				//Call disconnect callback
				eventManager.InvokeOnEvent(EVENT_DISCONNECT, null);
			}
		}
	}
}

