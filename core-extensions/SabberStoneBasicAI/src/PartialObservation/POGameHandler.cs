﻿using System;
using System.Linq;
using System.Diagnostics;
using SabberStoneCore.Config;
using SabberStoneCore.Enums;
using SabberStoneCore.Model.Entities;
using SabberStoneBasicAI.AIAgents;
using SabberStoneCore.Tasks.PlayerTasks;
using System.Collections.Generic;
using SabberStoneCore.Model;
using System.IO;
using System.Text;

namespace SabberStoneBasicAI.PartialObservation
{
	class POGameHandler
	{
		private AbstractAgent player1;
		private AbstractAgent player2;

		private GameConfig gameConfig;
		private bool setupHeroes = true;

		private GameStats gameStats;
		private static readonly Random Rnd = new Random();
		private bool repeatDraws = false;
		private int maxTurns = 50;

		private List<int> turnsRecords = new List<int>();

		private Queue<List<double>> trainingData = new Queue<List<double>>();

		public POGameHandler(GameConfig gameConfig, AbstractAgent player1, AbstractAgent player2, bool setupHeroes = true, bool repeatDraws = false)
		{
			this.gameConfig = gameConfig;
			this.setupHeroes = setupHeroes;
			this.player1 = player1;
			player1.InitializeAgent();

			this.player2 = player2;
			player2.InitializeAgent();

			gameStats = new GameStats();
		}

		public bool PlayGame(bool addToGameStats = true, bool debug = false)
		{
			SabberStoneCore.Model.Game game = new SabberStoneCore.Model.Game(gameConfig, setupHeroes);
			//var game = new Game(gameConfig, setupHeroes);
			player1.InitializeGame();
			player2.InitializeGame();

			AbstractAgent currentAgent;
			Stopwatch currentStopwatch;
			POGame poGame;
			PlayerTask playertask = null;
			Stopwatch[] watches = new[] { new Stopwatch(), new Stopwatch() };
			bool printGame = false;
			int numturns = 0;

			game.StartGame();
			if (gameConfig.SkipMulligan == false)
			{
				var originalStartingPlayer = game.CurrentPlayer;
				var originalStartinOpponent = game.CurrentOpponent;

				game.CurrentPlayer = originalStartingPlayer;
				currentAgent = gameConfig.StartPlayer == 1 ? player1 : player2;
				poGame = new POGame(game, debug);
				playertask = currentAgent.GetMove(poGame);
				game.Process(playertask);

				game.CurrentPlayer = originalStartinOpponent;
				currentAgent = gameConfig.StartPlayer == 1 ? player2 : player1;
				poGame = new POGame(game, debug);
				playertask = currentAgent.GetMove(poGame);
				game.Process(playertask);

				game.CurrentPlayer = originalStartingPlayer;
				game.MainReady();
			}
#if DEBUG
			try
			{
#endif
				while (game.State != State.COMPLETE && game.State != State.INVALID)
				{
					//if (debug)
					numturns = game.Turn;
					//Console.WriteLine("Turn " + game.Turn); 


					if(game.Turn == 15)
					{
						OutputCurrentGameForTrainingData(game);
					}
					//ShowLog(game, LogLevel.INFO);


					if (printGame)
					{
						//Console.WriteLine(MCGS.SabberHelper.SabberUtils.PrintGame(game));
						printGame = false;
					}

					if (game.Turn >= maxTurns)
						break;

					currentAgent = game.CurrentPlayer == game.Player1 ? player1 : player2;
					Controller currentPlayer = game.CurrentPlayer;
					currentStopwatch = game.CurrentPlayer == game.Player1 ? watches[0] : watches[1];
					poGame = new POGame(game, debug);

					currentStopwatch.Start();
					playertask = currentAgent.GetMove(poGame);
					currentStopwatch.Stop();

					game.CurrentPlayer.Game = game;
					game.CurrentOpponent.Game = game;

					if (debug)
					{
						//Console.WriteLine(playertask);
					}

					if (playertask.PlayerTaskType == PlayerTaskType.END_TURN)
						printGame = true;

					game.Process(playertask);
				}
#if DEBUG
			}
			catch (Exception e)
			//Current Player loses if he throws an exception
			{
				Console.WriteLine(e.Message);
				Console.WriteLine(e.StackTrace);
				game.State = State.COMPLETE;
				game.CurrentPlayer.PlayState = PlayState.CONCEDED;
				game.CurrentOpponent.PlayState = PlayState.WON;

				if (addToGameStats && game.State != State.INVALID)
					gameStats.registerException(game, e);
			}
#endif

			if (game.State == State.INVALID || (game.Turn >= maxTurns && repeatDraws))
				return false;

			if (addToGameStats)
				gameStats.addGame(game, watches);

			player1.FinalizeGame();
			player2.FinalizeGame();

			turnsRecords.Add(numturns);
			//ShowLog(game, LogLevel.INFO);

			return true;
		}

		public void PlayGames(int nr_of_games, bool addResultToGameStats = true, bool debug = false)
		{
			//ProgressBar pb = new ProgressBar(nr_of_games, 30, debug);

			for (int i = 0; i < nr_of_games; i++)
			{
				if (!PlayGame(addResultToGameStats, debug))
					i -= 1;     // invalid game
								//pb.Update(i);
								//Console.WriteLine("play game " + i + " out of " + nr_of_games);
			}
			double averageTurns = 0.0;
			foreach(int turns in turnsRecords)
			{
				averageTurns += turns;
			}
			averageTurns /= turnsRecords.Count;
			turnsRecords = turnsRecords.OrderBy((x => x)).ToList();
			Console.WriteLine("Average Turns: " + averageTurns);
			Console.WriteLine("20% Turns: " + turnsRecords[turnsRecords.Count/5]);
			Console.WriteLine("80% Turns: " + turnsRecords[turnsRecords.Count*4/5]);
			PrintTrainingData();
		}

		public GameStats getGameStats()
		{
			return gameStats;
		}


		internal static void ShowLog(Game game, LogLevel level)
		{
			var str = new StringBuilder();
			while (game.Logs.Count > 0)
			{
				LogEntry logEntry = game.Logs.Dequeue();
				if (logEntry.Level <= level)
				{
					ConsoleColor foreground = ConsoleColor.White;
					switch (logEntry.Level)
					{
						case LogLevel.DUMP:
							foreground = ConsoleColor.DarkCyan;
							break;
						case LogLevel.ERROR:
							foreground = ConsoleColor.Red;
							break;
						case LogLevel.WARNING:
							foreground = ConsoleColor.DarkRed;
							break;
						case LogLevel.INFO:
							foreground = logEntry.Location.Equals("Game") ? ConsoleColor.Yellow :
										 logEntry.Location.StartsWith("Quest") ? ConsoleColor.Cyan :
										 ConsoleColor.Green;
							break;
						case LogLevel.VERBOSE:
							foreground = ConsoleColor.DarkGreen;
							break;
						case LogLevel.DEBUG:
							foreground = ConsoleColor.DarkGray;
							break;
						default:
							throw new ArgumentOutOfRangeException();
					}

					Console.ForegroundColor = foreground;

					string logStr = logEntry.ToString();
					str.Append(logStr + "\n");
					Console.WriteLine(logStr);
				}
			}
			Console.ResetColor();

			File.WriteAllText(Directory.GetCurrentDirectory() + @"\dump.log", str.ToString());
		}


		public void OutputCurrentGameForTrainingData(Game game)
		{
			List<double> features = new List<double>();
			features.Add(game.Turn);
			features.Add(game.CurrentPlayer.Hero.Health);
			features.Add(game.CurrentOpponent.Hero.Health);
			trainingData.Enqueue(features);
		}

		public void PrintTrainingData()
		{
			StringBuilder sb = new StringBuilder();
			while(trainingData.Count > 0)
			{
				List<double> data = trainingData.Dequeue();
				for (int i = 0; i < data.Count; i++)
				{
					sb.Append(data[i].ToString());
					if(i != data.Count - 1)
					{
						sb.Append(", ");
					}
				}
				sb.Append("\n");
			}
			File.WriteAllText(Directory.GetCurrentDirectory() + @"\trainingData.txt", sb.ToString());
		}
	}

}
