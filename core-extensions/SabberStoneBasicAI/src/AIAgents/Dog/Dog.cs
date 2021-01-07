using System;
using System.Linq;
using System.Collections.Generic;
using SabberStoneCore.Tasks.PlayerTasks;
using SabberStoneBasicAI.PartialObservation;
using SabberStoneCore.Model.Entities;

namespace SabberStoneBasicAI.AIAgents.Dog
{
	class Dog : AbstractAgent
	{
		public Random Rnd = new Random();
		private Controller controller = null;
		private string rolloutPolicy = new string("epsilon-greedy");

		private const int NUM_ITERATIONS = 100;
		private const double EPSILON = 0.05;
		public override void InitializeAgent()
		{
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


			// For now the MCTS does not have memory.
			// It Creates a new MCTS node, simulates, and deletes it.
			MCTSNode realGame = new MCTSNode(null, null, poGame, null);

			Expansion(realGame);

			return null;

		}

		public MCTSNode Selection(MCTSNode node)
		{
			MCTSNode currNode = node;
			while(currNode.Children != null && currNode.Children.Count > 0)
			{
				currNode = currNode.UCTChildSelect();
			}
			return currNode;
		}

		public MCTSNode Expansion(MCTSNode node)
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
			var simulations = node.State.Simulate(node.State.CurrentPlayer.Options().Where(x => x.PlayerTaskType != PlayerTaskType.END_TURN).ToList()).Where((x => x.Value != null));

			node.Children = simulations.Select((x => new MCTSNode(node, null, x.Value, x.Key))).ToList();

			// Choose a new child to start rollout.
			// In MC, We can do so randomly, 
			// since they have just been created, and we don't care about instant rewards.
			return node.Children[Rnd.Next(0, node.Children.Count)];
		}

		public double rollout(MCTSNode node)
		{
			MCTSNode currNode = node;
			POGame currGame = node.State;
			// Early stop if the only option is END_TURN
			List<PlayerTask> options = currNode.State.CurrentPlayer.Options();
			while(options.Count > 1)
			{
				if (rolloutPolicy.Equals(new string("epsilon-greedy")))
				{
					if(Rnd.NextDouble() < EPSILON)
					{
						PlayerTask task = options[Rnd.Next(0, options.Count)];
						// If task chosen is END_TURN, do not simulate.
						if(task.PlayerTaskType == PlayerTaskType.END_TURN)
						{
							break;
						} else
						{
							// Do not simulate END_TURN,
							Dictionary<PlayerTask, POGame> simulations = 
								currNode.State.Simulate(options.Where(x => x.PlayerTaskType != PlayerTaskType.END_TURN).ToList());
							var nextStates = simulations.Select(x => x.Value).ToList();

							// rather, add it into next states.
							nextStates.Add(currNode.State);
							//!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
							currNode = nextStates.OrderBy(x => Evaluate(x)).Last();

						}
					}
				} else if(rolloutPolicy.Equals(new string("greedy")))
				{

				} else if(rolloutPolicy.Equals(new string("random")))
				{

				} else
				{

				}
				//!!!!!!!!!!!!!!!!!!!!!!!!!!
				options = currNode.State.CurrentPlayer.Options();
			}

			return 0.0;
		}

		//public MCTSNode UCBChoice(MCTSNode node)
		//{
		//	if (node.Children == null)
		//	{
		//		Console.WriteLine("ERROR: UCB on a node with no children!");
		//		return UCBChoice(Expansion(node));
		//	}
		//	return null;
		//}

		public double Evaluate(POGame state)
		{
			if(state.CurrentPlayer != controller)
			{
				Console.WriteLine("ERROR: Evaluating opponent's state!");
				return new DogZooLockScore { Controller = state.CurrentOpponent }.Evaluate();
			}
			return new DogZooLockScore { Controller = state.CurrentPlayer }.Evaluate();
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


		private const double VALUE_FOR_UNEXPLORED_STATE = 0;
		public int N = 0; 
		public int T = 0;

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
			return N == 0 ? VALUE_FOR_UNEXPLORED_STATE : T / N + Math.Sqrt(2 * Math.Log(Parent.N) / N);
		}
		public double Y6() { return N == 0 ? VALUE_FOR_UNEXPLORED_STATE : T / N; }

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
				result += OpMinionTotHealthTaunt / 30;

			result += MinionTotAtk / 50;

			result += (30 - OpHeroHp) / 30;

			result -= (30 - HeroHp) / 90;

			result = Math.Clamp(result, -1.0, 1.0);

			return result; 
		}

		public override Func<List<IPlayable>, List<int>> MulliganRule()
		{
			return p => p.Where(t => t.Cost > 3).Select(t => t.Id).ToList();
		}
	}

}
