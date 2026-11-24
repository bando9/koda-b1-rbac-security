import { useContext } from "react";
import { AuthContext } from "../context/auth-context";

function UseAuth() {
  const context = useContext(AuthContext);

  if (context === null) {
    throw new Error("use Auth harus digunakan didalam AuthProvider");
  }

  return context;
}

export default UseAuth;
