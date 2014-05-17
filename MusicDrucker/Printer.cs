using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections;
using System.Collections.Specialized;
using Android.Util;

namespace MusicDrucker
{
	class Printer
	{
		#region variables

		private string phost;
		private string pqueue;
		private string puser;

		private string errormsg = "";
		private string logfile = "";

		private const int pport = 515;			// hard coded LPR/LPD port number

		private string controlfile;				// not really a file but ok
		// Example: "HPC1\nProb\nfdfA040PC1\nUdfA040PC1\nNtimerH.ps\n"
		// H PC1   			=> responsible host (mandatory)
		// P rob 			=> responsible user (mandatory)
		// f dfA040PC1 		=> Print formatted file
		// U dfA040PC1      => Unlink data file (indicates that sourcefile is no longer needed!)
		// N timerH.ps      => Name of source file

		private Queue PrintQueue = new Queue();	// to keep the files to send

		#endregion variables

		#region constructor

		/// <summary>
		/// Constructor for an instance of an printer that can communicate 
		/// with LPR, LPQ and LPRM.
		/// </summary>
		/// <param name="printerName">idem</param>
		/// <param name="queueName">idem</param>
		/// <param name="userName">idem</param>
		public Printer(string printerName, string queueName, string userName)
		{
			phost = printerName;
			pqueue = queueName;
			puser = userName;
			string msg = string.Format("PRINTER: {0} {1} {2}", phost, pqueue, puser);
			WriteLog(msg);
		}

		#endregion constructor

		#region properties

		public string PHost
		{
			get
			{
				return phost;
			}
		}

		public string PQueue
		{
			get
			{
				return pqueue;
			}
			set
			{
				pqueue = value;
			}
		}

		public string PUser
		{
			get
			{
				return puser;
			}
		}

		public string LogFile
		{
			get
			{
				return logfile;
			}
			set
			{
				logfile = value;
				WriteLog("logfile -> " + logfile);
			}
		}

		public string ErrorMsg
		{
			get
			{
				return errormsg;
			}
		}

		public string Status
		{
			get
			{
				int cnt = InternalQueueSize;
				if (cnt > 0)
				{
					return "Busy.";
				}
				return "Idle.";
			}
		}

		public int InternalQueueSize
		{
			get
			{
				if (PrintQueue != null)
				{
					return PrintQueue.Count;
				}
				return 0;	// incorrect but ok
			}
		}


		#endregion properties

		#region Restart

		public void Restart()
		{
			WriteLog("Restart");
			ProcessRestart();
		}

		/// <summary>
		/// This command starts the printing process if it not already running.
		/// </summary>
		private void ProcessRestart()
		{
			errormsg = "";

			////////////////////////////////////////////////////////
			/// PREPARE TCPCLIENT STUF
			///
			TcpClient tc = new TcpClient();
			tc.Connect(phost, pport);
			NetworkStream nws = tc.GetStream();
			if (!nws.CanWrite)
			{
				errormsg = "-1: cannot write to network stream";
				Log.Error ("MusicDrucker-Printer", errormsg);
				nws.Close();
				tc.Close();
				return;
			}

			////////////////////////////////////////////////////////
			/// LOCAL VARIABLES
			///
			const int BUFSIZE = 1024;				// 1KB buffer 
			byte[] buffer = new byte[BUFSIZE];
			byte[] ack = new byte[4];				// for acknowledge
			int cnt;								// for read acknowledge

			////////////////////////////////////////////////////////
			/// COMMAND: RESTART
			///      +----+-------+----+
			///      | 01 | Queue | LF |
			///      +----+-------+----+
			///      Command code - 1
			///      Operand - Printer queue name
			/// 

			int pos = 0;
			buffer[pos++] = 1;
			for (int i = 0; i < pqueue.Length; i++)
			{
				buffer[pos++] = (byte)pqueue[i];
			}
			buffer[pos++] = (byte)'\n';

			nws.Write(buffer, 0, pos);
			nws.Flush();

			/////////////////////////////////////////////////////////
			/// READ ACK
			cnt = nws.Read(ack, 0, 4);
			if (ack[0] != 0)
			{
				errormsg = "-2: no ACK on RESTART";
				Log.Error ("MusicDrucker-Printer", errormsg);
				nws.Close();
				tc.Close();
				return;
			}
			nws.Close();
			tc.Close();
		}

		#endregion Restart

		#region LPR

		/// <summary>
		/// LPR starts a new thread to send a file to the LPD daemon. The 
		/// advantage of a separate thread per spool file is that the average 
		/// waiting time for jobs is minimized as small files do not have to 
		/// wait for large files.
		/// </summary>
		/// <param name="fileName">path to the file to be printed</param>
		public void LPR(string fileName)
		{
			string msg = "LPR: " + fileName;
			WriteLog(msg);
			if (!File.Exists(fileName))
			{
				errormsg = "-10: " + fileName + " does not exist.";
				Log.Error ("MusicDrucker-Printer", errormsg);
				return;
			}

			// start every print as a separate thread;
			// 
			// implementation of deleteAfterPrintFlag
			// thanx to Dion Slijp
//			ParameterizedThreadStart myThreadDelegate = new ParameterizedThreadStart(ProcessLPR);
//			Thread myThread = new Thread(myThreadDelegate);
//			myThread.Start(fileName);
			ProcessLPR (fileName);

			return;
		}
			
		// hacked by srinkes
		public void ProcessLPR(StringCollection fileNames)
		{
			if (fileNames != null)
			{
				foreach (string fn in fileNames)
				{
					ProcessLPR(fn);
				}
			}
		}

		// hacked by srinkes
		public void ProcessLPR(string fileName)
		{
			try
			{
				SendFile(fileName);
			}
			catch (Exception ex)
			{
				errormsg = "-11: " + ex.Message + " " + fileName;
				Log.Error ("MusicDrucker-Printer", errormsg);
			}
		}


		/// <summary>
		/// Internal worker to send the file over LPR. If the sending succeeds 
		/// and the del flag is TRUE the file <para>fname</para> will be deleted.
		/// If any error occurs the file will not be deleted.  
		/// </summary>
		/// <param name="fname">filename to send</param>
		/// <param name="del">flag delete after print</param>
		private void SendFile(string fname)
		{
			errormsg = "";

			////////////////////////////////////////////////////////
			/// PREPARE TCPCLIENT
			///
			TcpClient tc = new TcpClient();
			tc.Connect(phost, pport);
			NetworkStream nws = tc.GetStream();
			if (!nws.CanWrite)
			{
				errormsg = "-20: cannot write to network stream";
				Log.Error ("MusicDrucker-Printer", errormsg);
				nws.Close();
				tc.Close();
				return;
			}

			////////////////////////////////////////////////////////
			/// SOME LOCAL VARIABLES
			///
			string localhost = Dns.GetHostName();
			int jobID = GetJobId();
			string dname = String.Format("dfA{0}{1}", jobID, localhost);
			string cname = String.Format("cfA{0}{1}", jobID, localhost);
			controlfile = String.Format("H{0}\nP{1}\nf{2}\nU{3}\nN{4}\n",
				localhost, puser, dname, dname, Path.GetFileName(fname));

			const int BUFSIZE = 4 * 1024;			// 4KB buffer
			byte[] buffer = new byte[BUFSIZE];		// 
			byte[] ack = new byte[4];				// for the acknowledges
			int cnt;								// for read acknowledge

			////////////////////////////////////////////////////////
			/// COMMAND: RECEIVE A PRINTJOB
			///      +----+-------+----+
			///      | 02 | Queue | LF |
			///      +----+-------+----+
			///
			int pos = 0;
			buffer[pos++] = 2;
			for (int i = 0; i < pqueue.Length; i++)
			{l
				buffer[pos++] = (byte)pqueue[i];
			}
			buffer[pos++] = (byte)'\n';

			nws.Write(buffer, 0, pos);
			nws.Flush();

			/////////////////////////////////////////////////////////
			/// READ ACK
			cnt = nws.Read(ack, 0, 4);
			if (ack[0] != 0)
			{
				errormsg = "-21: no ACK on COMMAND 02.";
				Log.Error ("MusicDrucker-Printer", errormsg);
				nws.Close();
				tc.Close();
				return;
			}

			/////////////////////////////////////////////////////////
			/// SUBCMD: RECEIVE CONTROL FILE
			///
			///      +----+-------+----+------+----+
			///      | 02 | Count | SP | Name | LF |
			///      +----+-------+----+------+----+
			///      Command code - 2
			///      Operand 1 - Number of bytes in control file
			///      Operand 2 - Name of control file
			///
			pos = 0;
			buffer[pos++] = 2;
			string len = controlfile.Length.ToString();
			for (int i = 0; i < len.Length; i++)
			{
				buffer[pos++] = (byte)len[i];
			}
			buffer[pos++] = (byte)' ';
			for (int i = 0; i < cname.Length; i++)
			{
				buffer[pos++] = (byte)cname[i];
			}
			buffer[pos++] = (byte)'\n';

			nws.Write(buffer, 0, pos);
			nws.Flush();

			/////////////////////////////////////////////////////////
			/// READ ACK
			cnt = nws.Read(ack, 0, 4);
			if (ack[0] != 0)
			{
				errormsg = "-22: no ACK on SUBCMD 2";
				Log.Error ("MusicDrucker-Printer", errormsg);
				nws.Close();
				tc.Close();
				return;
			}

			/////////////////////////////////////////////////////////
			/// ADD CONTENT OF CONTROLFILE
			pos = 0;
			for (int i = 0; i < controlfile.Length; i++)
			{
				buffer[pos++] = (byte)controlfile[i];
			}
			buffer[pos++] = 0;

			nws.Write(buffer, 0, pos);
			nws.Flush();

			/////////////////////////////////////////////////////////
			/// READ ACK
			cnt = nws.Read(ack, 0, 4);
			if (ack[0] != 0)
			{
				errormsg = "-23: no ACK on CONTROLFILE";
				Log.Error ("MusicDrucker-Printer", errormsg);
				nws.Close();
				tc.Close();
				return;
			}

			/////////////////////////////////////////////////////////
			/// SUBCMD: RECEIVE DATA FILE
			///
			///      +----+-------+----+------+----+
			///      | 03 | Count | SP | Name | LF |
			///      +----+-------+----+------+----+
			///      Command code - 3
			///      Operand 1 - Number of bytes in data file
			///      Operand 2 - Name of data file
			///
			pos = 0;
			buffer[pos++] = 3;

			FileInfo DataFileInfo = new FileInfo(fname);
			len = DataFileInfo.Length.ToString();

			for (int i = 0; i < len.Length; i++)
			{
				buffer[pos++] = (byte)len[i];
			}
			buffer[pos++] = (byte)' ';
			for (int i = 0; i < dname.Length; i++)
			{
				buffer[pos++] = (byte)dname[i];
			}
			buffer[pos++] = (byte)'\n';

			nws.Write(buffer, 0, pos);
			nws.Flush();

			/////////////////////////////////////////////////////////
			/// READ ACK
			cnt = nws.Read(ack, 0, 4);
			if (ack[0] != 0)
			{
				errormsg = "-24: no ACK on SUBCMD 3";
				Log.Error ("MusicDrucker-Printer", errormsg);
				nws.Close();
				tc.Close();
				return;
			}

			/////////////////////////////////////////////////////////
			/// ADD CONTENT OF DATAFILE

			// use BinaryReader as print files may contain non ASCII characters.
			//			FileStream fs = new FileStream(fname, FileMode.Open, FileAccess.Read);
			//        	BinaryReader br = new BinaryReader(fs);
			//        	long totalbytes = 0;
			//            while (br.PeekChar() > -1)
			//            {
			//				int n = br.Read(buffer, 0, BUFSIZE);
			//				totalbytes += n;
			//	            nws.Write(buffer, 0, n);
			//            	nws.Flush();
			//            }
			//			br.Close();
			//			fs.Close();

			// Code Patched
			// thanx to Karl Fleishmann
			long totalbytes = 0;
			int bytesRead = 0;
			FileStream fstream = new FileStream(fname, FileMode.Open);
			while ((bytesRead = fstream.Read(buffer, 0, BUFSIZE)) > 0)
			{
				totalbytes += bytesRead;
				nws.Write(buffer, 0, bytesRead);
				nws.Flush();
			}
			fstream.Close();


			if (DataFileInfo.Length != totalbytes)
			{
				string msg = fname + ": file length error";
				WriteLog(msg);
				// just proceed for now
			}

			// close data file with a 0 ..
			pos = 0;
			buffer[pos++] = 0;
			nws.Write(buffer, 0, pos);
			nws.Flush();

			/////////////////////////////////////////////////////////
			/// READ ACK
			cnt = nws.Read(ack, 0, 4);
			if (ack[0] != 0)
			{
				errormsg = "-25: no ACK on DATAFILE";
				Log.Error ("MusicDrucker-Printer", errormsg);
				nws.Close();
				tc.Close();
				return;
			}

			nws.Close();
			tc.Close();
		}

		#endregion LPR

		#region LPQ

		/// <summary>
		/// LPQ requests the device for the queue content in a short format. 
		/// </summary>
		/// <returns>LPQ returns a printer specific string representing the queue</returns>
		public string LPQ()
		{
			return LPQ(false);
		}

		/// <summary>
		/// LPQ requests the device for the queue content. 
		/// If the parameter longlist is false it is requested in a short 
		/// format otherwise in a log format. Note that these formats are 
		/// not defined by the rfc1179 and are therefor printer or printserver 
		/// specific.
		/// </summary>
		/// <param name="longlist">boolean indicating a long (true) or a short (false) listing</param>
		/// <returns>LPQ returns a printer specific string representing the queue</returns>
		public string LPQ(bool longlist)
		{
			string msg = "LPQ: " + longlist.ToString();
			WriteLog(msg);

			string rv = "";
			try
			{
				rv = ProcessLPQ(longlist);
			}
			catch
			{
				errormsg = "-30:  Could not request queue";
				Log.Error ("MusicDrucker-Printer", errormsg);
				rv = errormsg;
			}
			return rv;
		}

		/// <summary>
		/// LPQ internal worker 
		/// </summary>
		/// <param name="longlist">long (true) or a short (false) listing</param>
		/// <returns>string representing the queue contents.</returns>
		private string ProcessLPQ(bool longlist)
		{
			errormsg = "";

			////////////////////////////////////////////////////////
			/// PREPARE TCPCLIENT STUF
			///
			TcpClient tc = new TcpClient();
			tc.Connect(phost, pport);
			NetworkStream nws = tc.GetStream();
			if (!nws.CanWrite)
			{
				errormsg = "-40: cannot write to network stream";
				Log.Error ("MusicDrucker-Printer", errormsg);
				nws.Close();
				tc.Close();
				return "";
			}

			////////////////////////////////////////////////////////
			/// SOME LOCAL VARS
			///
			const int BUFSIZE = 1024;				// 1KB buffer 
			byte[] buffer = new byte[BUFSIZE];		// fat buffer
			int cnt;								// for read acknowledge

			////////////////////////////////////////////////////////
			/// COMMAND: SEND QUEUE STATE
			///     +----+-------+----+------+----+
			///	    | 03 | Queue | SP | List | LF |
			///		+----+-------+----+------+----+
			/// 	Command code - 3 for short que listing 4 for long que listing
			///		Operand 1 - Printer queue name
			///		Other operands - User names or job numbers
			///
			int pos = 0;
			buffer[pos++] = (byte)(longlist ? 3 : 4);
			for (int i = 0; i < pqueue.Length; i++)
			{
				buffer[pos++] = (byte)pqueue[i];
			}
			buffer[pos++] = (byte)' ';
			// ask all users

			// for (int i = 0; i < user.Length; i++)
			// {
			// 		buffer[pos++] = (byte)user[i];
			// }
			buffer[pos++] = (byte)'\n';

			nws.Write(buffer, 0, pos);
			nws.Flush();

			/////////////////////////////////////////////////////////
			/// READ LPQ OUTPUT
			/// 
			string rv = "";
			buffer = new byte[BUFSIZE];
			cnt = nws.Read(buffer, 0, BUFSIZE);
			while (cnt > 0)
			{
				rv += System.Text.Encoding.ASCII.GetString(buffer);
				buffer = new byte[BUFSIZE];
				cnt = nws.Read(buffer, 0, BUFSIZE);
			}

			nws.Close();
			tc.Close();

			return rv.Replace("\n", "\r\n");
		}

		#endregion LPQ

		#region LPRM

		/// <summary>
		/// LPRM removes the job with jobID from the queue. If the jobID is 
		/// ommitted, the active job will be removed. Note that only jobs from 
		/// the current user can be removed. Check with LPQ if jobs are really 
		/// removed.
		/// </summary>
		/// <param name="jobID">
		/// String representing the jobID to be removed from the queue.
		/// jobID might contain multiple jobID's that are separated by spaces
		/// (at least for the winXP LPD daemon)
		/// </param>
		public void LPRM(string jobID)
		{
			string msg = "LPRM: " + jobID;
			WriteLog(msg);

			try
			{
				ProcessLPRM(jobID);
			}
			catch
			{
				errormsg = "-50: Could not remove job";
				Log.Error ("MusicDrucker-Printer", errormsg);
			}
			return;
		}

		/// <summary>
		/// LPRM internal worker
		/// </summary>
		/// <returns></returns>
		private void ProcessLPRM(string jobID)
		{
			errormsg = "";

			////////////////////////////////////////////////////////
			/// PREPARE TCPCLIENT STUF
			///
			TcpClient tc = new TcpClient();
			tc.Connect(phost, pport);
			NetworkStream nws = tc.GetStream();
			if (!nws.CanWrite)
			{
				errormsg = "-51: cannot write to network stream";
				Log.Error ("MusicDrucker-Printer", errormsg);
				nws.Close();
				tc.Close();
				return;
			}

			////////////////////////////////////////////////////////
			/// LOCAL VARIABLES
			///
			const int BUFSIZE = 1024;				// 1KB buffer
			byte[] buffer = new byte[BUFSIZE];		// buffer
			int cnt;								// for read acknowledge
			byte[] ack = new byte[4];				// for the acknowledges

			////////////////////////////////////////////////////////
			/// COMMAND: REMOVE JOBS 
			///      +----+-------+----+-------+----+------+----+
			///      | 05 | Queue | SP | Agent | SP | List | LF |
			///      +----+-------+----+-------+----+------+----+
			///      Command code - 5
			///      Operand 1 - Printer queue name
			///      Operand 2 - User name making request (the agent)
			///      Other operands - User names or job numbers
			///

			int pos = 0;
			buffer[pos++] = 5;
			for (int i = 0; i < pqueue.Length; i++)
			{
				buffer[pos++] = (byte)pqueue[i];
			}
			buffer[pos++] = (byte)' ';

			// for current user
			for (int i = 0; i < puser.Length; i++)
			{
				buffer[pos++] = (byte)puser[i];
			}

			// identified job, might be an empty string
			buffer[pos++] = (byte)' ';
			for (int i = 0; i < jobID.Length; i++)
			{
				buffer[pos++] = (byte)jobID[i];
			}
			buffer[pos++] = (byte)'\n';

			nws.Write(buffer, 0, pos);
			nws.Flush();

			/////////////////////////////////////////////////////////
			/// READ LPRM OUTPUT
			cnt = nws.Read(ack, 0, 4);
			if (ack[0] != 0)
			{
				errormsg = "-52: no ACK on COMMAND 05";
				Log.Error ("MusicDrucker-Printer", errormsg);
				nws.Close();
				tc.Close();
				return;
			}

			nws.Close();
			tc.Close();

			return;
		}

		#endregion LPRM

		#region misc
		/// <summary>
		/// GetCounter returns the next jobid for the LPR protocol
		/// which must be between 0 and 999.
		/// The jobid is incremented every call but will be wrapped to 0 when 
		/// larger than 999. 
		/// </summary>
		/// <returns>next number</returns>
		private int GetJobId()
		{
			// TODO: GetJobID: keep counter in the registry, or use random generator.
			string temp = Environment.GetEnvironmentVariable("TEMP");
			string cntpath = temp + @"\LprJobId.txt";

			int cnt = 0;
			try
			{
				StreamReader sr = new StreamReader(cntpath);
				cnt = Int32.Parse(sr.ReadLine());
				sr.Close();
			}
			catch	// file doesn't exist
			{
				cnt = 0;
			}
			cnt++;			// next number but 
			cnt %= 1000;	// keep cnt between 0 and 999
			try
			{
				StreamWriter sw = new StreamWriter(cntpath);
				sw.WriteLine("{0}\n", cnt);
				sw.Close();
			}
			catch
			{
			}
			return cnt;
		}

		/// <summary>
		/// WriteLog writes a message with a date to the logfile.
		/// </summary>
		/// <param name="message">string to write in the logfile.</param>
		private void WriteLog(string message)
		{
			string msg = DateTime.Now.ToString() + "; " + message;
			Log.Info ("Printer.cs", msg);
		}
		#endregion misc
	}
}