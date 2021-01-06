using System;
using System.Linq;
using System.Collections.Generic;
using SabberStoneCore.Tasks.PlayerTasks;
using SabberStoneBasicAI.PartialObservation;


namespace SabberStoneBasicAI.AIAgents.Dog
{
	class Dog : AbstractAgent
	{

		private const int NUM_ITERATIONS = 100;
		public override void InitializeAgent()
		{
		}

		public override void FinalizeAgent()
		{
		}

		public override void FinalizeGame()
		{
		}

		public override PlayerTask GetMove(POGame poGame)
		{
			List<PlayerTask> options = poGame.CurrentPlayer.Options();


			// For now the MCTS is non-memory.
			// It Creates a new MCTS node, simulates, and deletes it.
			MCTSNode realGame = new MCTSNode(null, null, poGame, null);
			Expansion(realGame);

			return null;

		}

		public override void InitializeGame()
		{

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

			// Simulate all options?
			Dictionary<PlayerTask, POGame> simulations = node.State.Simulate(node.State.CurrentPlayer.Options());

			node.Children = simulations.Select((x => new MCTSNode(node, null, x.Value, x.Key))).ToList();

			// Choose a new children???? UCB

			return null;
			
		}

		public MCTSNode UCBChoice(MCTSNode node)
		{
			if (node.Children == null)
			{
				Console.WriteLine("ERROR: UCB on a node with no children!");
				return UCBChoice(Expansion(node));
			}
			return null;
		}


	}

	class MCTSNode
	{
		public MCTSNode Parent = null;
		public List<MCTSNode> Children = null;

		private POGame m_state = null;
		public POGame State { get { return m_state; } }

		private PlayerTask m_action = null;
		public PlayerTask Action { get { return m_action; } }



		private const double VALUE_FOR_UNEXPLORED_STATE = 0;
		public int N = 0;
		public int T = 0;

		public MCTSNode() { }
		public MCTSNode(MCTSNode parent, List<MCTSNode> children, POGame state, PlayerTask action)
		{
			Parent = parent;
			Children = children;
			m_state = state;
			m_action = action;
		}
		
		public double Value() { return N == 0 ? VALUE_FOR_UNEXPLORED_STATE : T / N; }

	}
}
