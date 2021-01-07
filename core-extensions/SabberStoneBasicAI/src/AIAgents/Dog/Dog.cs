using System;
using System.Linq;
using System.Collections.Generic;
using SabberStoneCore.Tasks.PlayerTasks;
using SabberStoneBasicAI.PartialObservation;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Model.Zones;
using SabberStoneCore.Model;
using SabberStoneCore.Enums;
using System.Net.Sockets;
using System.Net;

namespace SabberStoneBasicAI.AIAgents.Dog
{
	class Dog : AbstractAgent
	{
		public Random Rnd = new Random();
		private Controller controller = null;
		private string rolloutPolicy = "random";

		public const int NUM_ITERATIONS = 1000;
		private const double EPSILON = 0.05;
		public static double ConstantForUCT = 0.0;
		public static Socket NNSocket = null;
		private int port = 2303;
		private int[] features = new int[169];
		private byte[] dataBuffer = new byte[169 * 4];
		private byte[] receiveBuffer = new byte[4];

		private const bool SOCKET_EVAL = true;

		public override void InitializeAgent()
		{
			ConstantForUCT = 1.0 / Math.Sqrt(Math.Log(Dog.NUM_ITERATIONS));

			initializeSocket();

		}

		private void initializeSocket()
		{
			IPAddress address = new IPAddress(new byte[4] { 10, 11, 135, 182 });
			IPEndPoint ipe = new IPEndPoint(address, port);
			Socket tempSocket = new Socket(ipe.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

			tempSocket.Connect(ipe);

			if (tempSocket.Connected)
			{
				NNSocket = tempSocket;
			}
			if (NNSocket == null)
			{
				Console.WriteLine("ERROR: Socket not connected!");
			}
		}

		public override void FinalizeAgent()
		{
		}

		public override void FinalizeGame()
		{
		}

		public override void InitializeGame()
		{

		}
		public override PlayerTask GetMove(POGame poGame)
		{
			if (controller == null)
			{
				controller = poGame.CurrentPlayer;
			}

			List<PlayerTask> options = poGame.CurrentPlayer.Options();
			if(options.Count == 1)
			{
				return options[0];
			}

			// For now the MCTS does not have memory.
			// It Creates a new MCTS node, simulates, and deletes it.
			MCTSNode realGame = new MCTSNode(null, null, poGame, null);

			while(realGame.N < NUM_ITERATIONS)
			{
				MCTSNode node = Select(realGame);
				node = Expand(node);
				double rolloutY6 = Rollout(node);
				BackPropagate(node, rolloutY6);
			}

			return realGame.Children.OrderBy(x => x.Y6()).Last().Action;

		}

		public MCTSNode Select(MCTSNode node)
		{
			MCTSNode currNode = node;
			while(currNode.Children != null && currNode.Children.Count > 0)
			{
				currNode = currNode.UCTChildSelect();
			}
			return currNode;
		}

		public MCTSNode Expand(MCTSNode node)
		{
			if(node.Children != null)
			{
				Console.WriteLine("ERROR: Expanding a node with children!");
				return node;
			}

			if(node.State == null)
			{
				Console.WriteLine("ERROR: Null state!");
				return null;
			}

			// If The only option is END_TURN, do not expand.
			if(node.State.CurrentPlayer.Options().Count == 1 &&
			   node.State.CurrentPlayer.Options()[0].PlayerTaskType == PlayerTaskType.END_TURN)
			{
				return node;
			}

			// Simulate all options except END_TURN.
			var simulations = node.State.Simulate(node.State.CurrentPlayer.Options().Where(x => x.PlayerTaskType != PlayerTaskType.END_TURN).ToList()).Where((x => x.Value != null)).ToList();

			if(simulations.Count == 0)
			{
				return node;
			}

			node.Children = simulations.Select((x => new MCTSNode(node, null, x.Value, x.Key))).ToList();

			//Add endturn as child

			// Choose a new child to start rollout.
			// In MC, We can do so randomly, 
			// since they have just been created, and we don't care about instant rewards.
			return node.Children[Rnd.Next(0, node.Children.Count)];
		}

		public double Rollout(MCTSNode node)
		{
			POGame currGame = node.State;
			List<PlayerTask> options = currGame.CurrentPlayer.Options();

			// Stop if the only option is END_TURN
			while (options.Count > 1)
			{
				if (rolloutPolicy.Equals(new string("epsilon-greedy")))
				{
					if (Rnd.NextDouble() < EPSILON)
					{
						POGame nextGame = null;
						do
						{
							PlayerTask task = options[Rnd.Next(0, options.Count)];
							// If task chosen is END_TURN, do not simulate.
							if (task.PlayerTaskType == PlayerTaskType.END_TURN)
							{
								return Evaluate(currGame);
							} else
							// Else, simulate the random task.
							{
								var listedTask = new List<PlayerTask>();
								listedTask.Add(task);
								nextGame = currGame.Simulate(listedTask).ToList()[0].Value;
							}
						} while (nextGame == null);
						currGame = nextGame;
					} else
					{
						// Do not simulate END_TURN,
						var simulations =
							currGame.Simulate(options.Where(x => x.PlayerTaskType != PlayerTaskType.END_TURN).ToList()).Where(x => x.Value != null).ToList();
						//var nextStates = simulations.Select(x => x.Value).ToList();

						// rather, add it into next states.
						//nextStates.Add(currGame);


						if (simulations.Count == 0)
						{
							break;
						}

						//!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
						var greedyActionChoice = simulations.OrderBy(x => Evaluate(x.Value)).Last();

						// If END_TURN is a better choice
						double endTurnValue = Evaluate(currGame);
						if (endTurnValue >= Evaluate(greedyActionChoice.Value))
						{
							return endTurnValue;
						} else
						{
							currGame = greedyActionChoice.Value;
						}
						

					}
				} 
				else if(rolloutPolicy.Equals(new string("greedy")))
				{
					// Do not simulate END_TURN,
					Dictionary<PlayerTask, POGame> simulations =
						currGame.Simulate(options.Where(x => x.PlayerTaskType != PlayerTaskType.END_TURN).ToList());

					if(simulations.Count == 0)
					{
						break;
					}

					//!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
					var greedyActionChoice = simulations.OrderBy(x => Evaluate(x.Value)).Last();

					// If END_TURN is a better choice
					double endTurnValue = Evaluate(currGame);
					if (endTurnValue >= Evaluate(greedyActionChoice.Value))
					{
						return endTurnValue;
					} else
					{
						currGame = greedyActionChoice.Value;
					}

				} 
				else if(rolloutPolicy.Equals(new string("random")))
				{
					POGame nextGame = null;
					do
					{
						PlayerTask task = options[Rnd.Next(0, options.Count)];

						// If task chosen is END_TURN, do not simulate.
						if (task.PlayerTaskType == PlayerTaskType.END_TURN)
						{
							return Evaluate(currGame);
						} else
						// Else, simulate the random task.
						{
							var listedTask = new List<PlayerTask>();
							listedTask.Add(task);
							nextGame = currGame.Simulate(listedTask).ToList()[0].Value;
						}
					} while (nextGame == null);
					currGame = nextGame;
				} else
				{
					Console.WriteLine("INVALID ROLLOUT POLICY");
					return 0.0;
				}

				// Safety check: current game is played by Dog Agent. 
				if(!currGame.CurrentPlayer.Name.Equals(controller.Name))
				{
					Console.WriteLine("ERROR: in rollout: Current player is opponent!");
				}
				options = currGame.CurrentPlayer.Options();
			}
			return Evaluate(currGame);
		}

		public void BackPropagate(MCTSNode terminalNode, double rolloutY6)
		{
			MCTSNode currNode = terminalNode;
			while(currNode != null)
			{
				currNode.N++;
				currNode.T += rolloutY6;
				currNode = currNode.Parent;
			}
		}

		public double Evaluate(POGame state)
		{
			if(SOCKET_EVAL && NNSocket != null)
			{
				return (double)SocketEvaluate(state.getGame());
			}

			if(!state.CurrentPlayer.Name.Equals(controller.Name))
			{
				Console.WriteLine("ERROR: Evaluating opponent's state!");
				return new DogZooLockScore { Controller = state.CurrentOpponent }.Evaluate();
			}
			return new DogZooLockScore { Controller = state.CurrentPlayer }.Evaluate();
		}


		public float SocketEvaluate(Game game)
		{
			//int[] features = new int[163];
			int count = 0;
			HandZone Hand = game.CurrentPlayer.HandZone;
			BoardZone BoardZone = game.CurrentPlayer.BoardZone;
			BoardZone OpBoardZone = game.CurrentPlayer.Opponent.BoardZone;
			Minion[] minions = BoardZone.GetAll();
			features[count++] = (game.Turn);
			features[count++] = ((int)game.CurrentPlayer.HeroClass);
			features[count++] = (game.CurrentPlayer.Hero.Weapon == null ? 0 : game.CurrentPlayer.Hero.Weapon.Card.AssetId);
			features[count++] = (game.CurrentPlayer.Hero.Weapon == null ? 0 : game.CurrentPlayer.Hero.Weapon.AttackDamage);
			features[count++] = (game.CurrentPlayer.Hero.Weapon == null ? 0 : game.CurrentPlayer.Hero.Weapon.Durability);
			features[count++] = (game.CurrentPlayer.Hero.Health);
			features[count++] = (game.CurrentPlayer.BaseMana);
			features[count++] = (game.CurrentPlayer.HandZone.Count);
			features[count++] = (game.CurrentPlayer.DeckZone.Count);
			features[count++] = (game.CurrentPlayer.BoardZone.Count);
			for (int i = 0; i < game.CurrentPlayer.HandZone.Count; i++)
			{
				features[count++] = (game.CurrentPlayer.HandZone[i].Card.AssetId);
			}
			for (int i = 0; i < 10 - game.CurrentPlayer.HandZone.Count; i++)
			{
				features[count++] = (0);
			}
			foreach (Minion minion in minions)
			{
				features[count++] = (minion.Card.AssetId);
				features[count++] = (minion[GameTag.ATK]);
				features[count++] = (minion[GameTag.HEALTH]);
				features[count++] = (minion[GameTag.DAMAGE]);
				features[count++] = (minion[GameTag.STEALTH]);
				features[count++] = (minion[GameTag.IMMUNE]);
				features[count++] = (minion[GameTag.TAUNT]);
				features[count++] = (minion[GameTag.CANT_BE_TARGETED_BY_SPELLS]);
				features[count++] = (minion[GameTag.NUM_ATTACKS_THIS_TURN]);
				features[count++] = (minion.Card.AssetId);
			}
			for (int _ = 0; _ < 7 - minions.Length; _++)
			{
				features[count++] = (0);
				features[count++] = (0);
				features[count++] = (0);
				features[count++] = (0);
				features[count++] = (0);
				features[count++] = (0);
				features[count++] = (0);
				features[count++] = (0);
				features[count++] = (0);
				features[count++] = (0);
			}
			features[count++] = ((int)game.CurrentOpponent.HeroClass);
			features[count++] = (game.CurrentOpponent.Hero.Weapon == null ? 0 : game.CurrentOpponent.Hero.Weapon.Card.AssetId);
			features[count++] = (game.CurrentOpponent.Hero.Weapon == null ? 0 : game.CurrentOpponent.Hero.Weapon.AttackDamage);
			features[count++] = (game.CurrentOpponent.Hero.Weapon == null ? 0 : game.CurrentOpponent.Hero.Weapon.Durability);
			features[count++] = (game.CurrentOpponent.Hero.Health);
			features[count++] = (game.CurrentOpponent.BaseMana);
			features[count++] = (game.CurrentOpponent.HandZone.Count);
			features[count++] = (game.CurrentOpponent.DeckZone.Count);
			features[count++] = (game.CurrentOpponent.BoardZone.Count);
			minions = OpBoardZone.GetAll();
			foreach (Minion minion in minions)
			{
				features[count++] = (minion.Card.AssetId);
				features[count++] = (minion[GameTag.ATK]);
				features[count++] = (minion[GameTag.HEALTH]);
				features[count++] = (minion[GameTag.DAMAGE]);
				features[count++] = (minion[GameTag.STEALTH]);
				features[count++] = (minion[GameTag.IMMUNE]);
				features[count++] = (minion[GameTag.TAUNT]);
				features[count++] = (minion[GameTag.CANT_BE_TARGETED_BY_SPELLS]);
				features[count++] = (minion[GameTag.NUM_ATTACKS_THIS_TURN]);
				features[count++] = (minion.Card.AssetId);
			}
			for (int _ = 0; _ < 7 - minions.Length; _++)
			{
				features[count++] = (0);
				features[count++] = (0);
				features[count++] = (0);
				features[count++] = (0);
				features[count++] = (0);
				features[count++] = (0);
				features[count++] = (0);
				features[count++] = (0);
				features[count++] = (0);
				features[count++] = (0);
			}
			
			// Safety check
			if(count != 169)
			{
				Console.WriteLine("ERROR: Incorrect parameter size!");
			}

			for(int i = 0; i < features.Length; i++)
			{
				byte[] tmpBytes = BitConverter.GetBytes(features[i]);
				dataBuffer[4 * i + 0] = tmpBytes[0];
				dataBuffer[4 * i + 1] = tmpBytes[1];
				dataBuffer[4 * i + 2] = tmpBytes[2];
				dataBuffer[4 * i + 3] = tmpBytes[3];
			}

			NNSocket.Send(dataBuffer);

			NNSocket.Receive(receiveBuffer, 4, SocketFlags.None);

			return BitConverter.ToSingle(receiveBuffer);

		}
	}

	class MCTSNode
	{
		private MCTSNode parent = null;
		internal MCTSNode Parent { get => parent; set => parent = value; }

		private List<MCTSNode> children = null;
		internal List<MCTSNode> Children { get => children; set => children = value; }

		private POGame state = null;
		public POGame State { get => state; set => state = value; }

		private PlayerTask action = null;
		public PlayerTask Action { get => action; set => action = value; }


		private const double VALUE_FOR_UNEXPLORED_STATE = 1;

		public int N = 0; 
		public double T = 0;

		public MCTSNode() { }
		public MCTSNode(MCTSNode parent, List<MCTSNode> children, POGame state, PlayerTask action)
		{
			Parent = parent;
			Children = children;
			State = state;
			Action = action;
		}

		public MCTSNode UCTChildSelect()
		{
			return Children?.Select(x => new KeyValuePair<MCTSNode, double>(x, x.UCTY6())).OrderBy(x => x.Value).Last().Key;
		}

		public double UCTY6()
		{
			if (Parent == null) return Y6();
			return N == 0 ? VALUE_FOR_UNEXPLORED_STATE : T / N + Dog.ConstantForUCT * Math.Sqrt(Math.Log(Parent.N) / N);
		}
		public double Y6() { return N == 0 ? 0 : T / N; }

	}

	class DogZooLockScore : Score.Score
	{
		public override int Rate()
		{
			return 0;
		}
		public double Evaluate()
		{
			if (OpHeroHp < 1)
				return 1;

			if (HeroHp < 1)
				return -1;

			double result = 0;

			if (OpBoardZone.Count == 0 && BoardZone.Count > 0)
				result += 0.25;

			if (OpMinionTotHealthTaunt > 0)
				result += OpMinionTotHealthTaunt / 30.0;

			result += (double)MinionTotAtk / 50.0;

			result -= (double)OpMinionTotAtk / 40.0;

			result += (30 - OpHeroHp) / 60.0;

			result -= (30 - HeroHp) / 90.0;

			result = Math.Clamp(result, -1.0, 1.0);

			return result; 
		}

		public override Func<List<IPlayable>, List<int>> MulliganRule()
		{
			return p => p.Where(t => t.Cost > 3).Select(t => t.Id).ToList();
		}
	}

}
