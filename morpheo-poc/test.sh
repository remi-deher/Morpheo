#!/bin/bash

# Port mappings:
# Server:   8080 (User API) / 5000 (Morpheo Sync)
# Client 1: 8081 (User API) / 5001 (Morpheo Sync)
# Client 2: 8082 (User API) / 5002 (Morpheo Sync)
# Client 3: 8083 (User API) / 5003 (Morpheo Sync)

COMMAND=$1
ARG1=$2
ARG2=$3

show_help() {
  echo "Morpheo CLI Testing Utility"
  echo "Usage:"
  echo "  ./test.sh add <node> \"Todo Title\"       - Add a todo on node (1, 2, 3 or server)"
  echo "  ./test.sh list <node>                     - List todos on node (1, 2, 3 or server)"
  echo "  ./test.sh delete <node> <todo_id>         - Delete a todo on node"
  echo "  ./test.sh status                          - Show status of all nodes"
  echo ""
  echo "Examples:"
  echo "  ./test.sh add 1 \"Complete presentation\""
  echo "  ./test.sh list 2"
  echo "  ./test.sh status"
}

get_port() {
  case "$1" in
    1|client1) echo "8081" ;;
    2|client2) echo "8082" ;;
    3|client3) echo "8083" ;;
    server|s) echo "8080" ;;
    *) echo "" ;;
  esac
}

if [ -z "$COMMAND" ]; then
  show_help
  exit 0
fi

case "$COMMAND" in
  add)
    PORT=$(get_port "$ARG1")
    if [ -z "$PORT" ]; then
      echo "Error: Invalid node name. Use 1, 2, 3, or server."
      exit 1
    fi
    if [ -z "$ARG2" ]; then
      echo "Error: Title is required. Example: ./test.sh add 1 \"Buy milk\""
      exit 1
    fi
    JSON="{\"title\":\"$ARG2\",\"isCompleted\":false}"
    echo "Adding Todo on node $ARG1 (port $PORT)..."
    curl -X POST -H "Content-Type: application/json" -d "$JSON" "http://localhost:$PORT/api/todos"
    echo ""
    ;;

  list)
    PORT=$(get_port "$ARG1")
    if [ -z "$PORT" ]; then
      echo "Error: Invalid node name. Use 1, 2, 3, or server."
      exit 1
    fi
    echo "Todos on node $ARG1 (port $PORT):"
    curl -s "http://localhost:$PORT/api/todos"
    echo ""
    echo ""
    ;;

  delete)
    PORT=$(get_port "$ARG1")
    if [ -z "$PORT" ]; then
      echo "Error: Invalid node name. Use 1, 2, 3, or server."
      exit 1
    fi
    if [ -z "$ARG2" ]; then
      echo "Error: Todo ID is required. Example: ./test.sh delete 1 \"<id>\""
      exit 1
    fi
    echo "Deleting Todo $ARG2 on node $ARG1 (port $PORT)..."
    curl -X DELETE "http://localhost:$PORT/api/todos/$ARG2"
    echo ""
    ;;

  status)
    for name in server client1 client2 client3; do
      case "$name" in
        server) PORT="8080" ;;
        client1) PORT="8081" ;;
        client2) PORT="8082" ;;
        client3) PORT="8083" ;;
      esac
      echo "=== Node: $name (Port $PORT) ==="
      curl -s --connect-timeout 2 "http://localhost:$PORT/api/status" || echo "Offline"
      echo ""
      echo ""
    done
    ;;

  *)
    show_help
    ;;
esac
