using System;
using System.Collections.Generic;
using System.Linq;
using Mars.Common;
using Mars.Interfaces.Agents;
using Mars.Interfaces.Annotations;
using Mars.Interfaces.Environments;
using Mars.Interfaces.Layers;
using Mars.Numerics;

namespace GridBlueprint.Model;

public class ComplexAgent : IAgent<GridLayer>, IPositionable
{
    #region Init

    /// <summary>
    ///     The initialization method of the ComplexAgent which is executed once at the beginning of a simulation.
    ///     It sets an initial Position and an initial State and generates a list of movement directions.
    /// </summary>
    /// <param name="layer">The GridLayer that manages the agents</param>
    public void Init(GridLayer layer)
    {
        _layer = layer;
        Position = new Position(X, Y);
        _state = AgentState.MoveWithBearing;
        _directions = CreateMovementDirectionsList();
        _layer.ComplexAgentEnvironment.Insert(this);
    }

    #endregion

    #region Tick

    /// <summary>
    ///     The tick method of the ComplexAgent which is executed during each time step of the simulation.
    ///     A ComplexAgent can move randomly along straight lines. It must stay within the bounds of the GridLayer
    ///     and cannot move onto grid cells that are not routable.
    /// </summary>
    public void Tick()
    {
        _state = RandomlySelectNewState();
        
        if (_state == AgentState.MoveRandomly)
        {
            MoveRandomly();
        }
        else if (_state == AgentState.MoveWithBearing)
        {
            // MoveWithBearing();
        }
        else if (_state == AgentState.MoveTowardsGoal)
        {
            MoveTowardsGoal();
        }
        else if (_state == AgentState.ExploreAgents)
        {
            ExploreAgents();
        }
        
        if (_layer.GetCurrentTick() == 595)
        {
            RemoveFromSimulation();
        }
    }

    #endregion

    #region Methods

    /// <summary>
    ///     Generates a list of eight movement directions that the agent uses for random movement.
    /// </summary>
    /// <returns>The list of movement directions</returns>
    private static List<Position> CreateMovementDirectionsList()
    {
        return new List<Position>
        {
            MovementDirections.North,
            MovementDirections.Northeast,
            MovementDirections.East,
            MovementDirections.Southeast,
            MovementDirections.South,
            MovementDirections.Southwest,
            MovementDirections.West,
            MovementDirections.Northwest
        };
    }
    
    /// <summary>
    ///     Performs one random move, if possible, using the movement directions list.
    /// </summary>
    private void MoveRandomly()
    {
        var nextDirection = _directions[_random.Next(_directions.Count)];
        var newX = Position.X + nextDirection.X;
        var newY = Position.Y + nextDirection.Y;
        
        // Check if chosen move is within the bounds of the grid
        if (0 <= newX && newX < _layer.Width && 0 <= newY && newY < _layer.Height)
        {
            // Check if chosen move goes to a cell that is routable
            if (_layer.IsRoutable(newX, newY))
            {
                Position = new Position(newX, newY);
                _layer.ComplexAgentEnvironment.MoveTo(this, new Position(newX, newY));
                Console.WriteLine($"{GetType().Name} moved to a new cell: {Position}");
            }
            else
            {
                Console.WriteLine($"{GetType().Name} tried to move to a blocked cell: ({newX}, {newY})");
            }
        }
        else
        {
            Console.WriteLine($"{GetType().Name} tried to leave the world: ({newX}, {newY})");
        }
    }

    /// <summary>
    ///     Moves the agent towards a random nearby cell via a calculated bearing.
    ///     // TODO: This does not work yet. How to consider non-routable grid cells during .MoveTowards() call?
    /// </summary>
    private void MoveWithBearing()
    {
        var goal = FindRoutableGoal();
        var bearing = PositionHelper.CalculateBearingCartesian(Position.X, Position.Y, goal.X, goal.Y);
        var curPos = Position;
        var newPos = _layer.ComplexAgentEnvironment.MoveTowards(this, bearing, 1);
        if (!_layer.IsRoutable(newPos))
        {
            Position = curPos;
            Console.WriteLine("Rollback");
        }
    }

    /// <summary>
    ///     Moves the agent one step along the shortest routable path towards a fixed goal.
    /// </summary>
    private void MoveTowardsGoal()
    {
        if (!_tripInProgress)
        {
            // Explore nearby grid cells based on their values
            _goal ??= FindRoutableGoal();
            _path = _layer.FindPath(Position, _goal).GetEnumerator();
            _tripInProgress = true;
        }
        
        if (_path.MoveNext())
        {
            _layer.ComplexAgentEnvironment.MoveTo(this, _path.Current, 1);
            if (Position.Equals(_goal))
            {
                Console.WriteLine($"ComplexAgent {ID} reached goal {_goal}");
                _goal = FindRoutableGoal();
                _tripInProgress = false;
            }
        }
    }

    /// <summary>
    ///     Finds a routable grid cell that serves as a goal for subsequent pathfinding.
    /// </summary>
    /// <returns>The found grid cell</returns>
    private Position FindRoutableGoal()
    {
        var nearbyRoutableCells = _layer.Explore(Position, radius: MaxTripDistance, predicate: cellValue => cellValue == 0.0).ToList();
        var goal = nearbyRoutableCells[_random.Next(nearbyRoutableCells.Count)].Node.NodePosition;
        
        while (Position.Equals(goal))
        {
            goal = nearbyRoutableCells[_random.Next(nearbyRoutableCells.Count)].Node.NodePosition;
        }
        
        Console.WriteLine($"New goal: {goal}");
        return goal;
    }

    /// <summary>
    ///     Explores the environment for agents of another type and increments their counter if they are nearby.
    /// </summary>
    private void ExploreAgents()
    {
        // Explore nearby other SimpleAgent instances
        var agents = _layer.SimpleAgentEnvironment.Explore(Position, radius: AgentExploreRadius);

        foreach (var agent in agents)
        {
            if (Distance.Chebyshev(new []{Position.X, Position.Y}, new []{agent.Position.X, agent.Position.Y}) <= 1.0)
            {
                agent.IncrementCounter();
            }
        }
    }

    /// <summary>
    ///     Selects a new state from the AgentState enumeration to guide for subsequent behavior.
    /// </summary>
    /// <returns>The selected state</returns>
    private AgentState RandomlySelectNewState()
    {
        if (_state == AgentState.MoveTowardsGoal && _tripInProgress)
        {
            Console.WriteLine("Trip still in progress, so no state change.");
            return AgentState.MoveTowardsGoal;
        }

        var agentStates = Enum.GetValues(typeof(AgentState));
        var newState = (AgentState) agentStates.GetValue(_random.Next(agentStates.Length))!;
        Console.WriteLine($"New state: {newState}");
        return newState;
    }

    /// <summary>
    ///     Removes this agent from the simulation and, by extension, from the visualization.
    /// </summary>
    private void RemoveFromSimulation()
    {
        Console.WriteLine($"ComplexAgent {ID} is removing itself from the simulation.");
        _layer.ComplexAgentEnvironment.Remove(this);
        UnregisterAgentHandle.Invoke(_layer, this);
    }

    #endregion

    #region Fields and Properties

    public Guid ID { get; set; }
    
    public Position Position { get; set; }

    [PropertyDescription(Name = "X")]
    public int X { get; set; }
    
    [PropertyDescription(Name = "Y")]
    public int Y { get; set; }
    
    [PropertyDescription(Name = "MaxTripDistance")]
    public double MaxTripDistance { get; set; }
    
    [PropertyDescription(Name = "AgentExploreRadius")]
    public double AgentExploreRadius { get; set; }
    
    public UnregisterAgent UnregisterAgentHandle { get; set; }
    
    private GridLayer _layer;
    private List<Position> _directions;
    private readonly Random _random = new();
    private Position _goal;
    private bool _tripInProgress;
    private AgentState _state;
    private List<Position>.Enumerator _path;

    #endregion
}